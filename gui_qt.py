"""Desktop front end for the layered IFC -> OBJ converter (PySide6).

The GUI sits above the layer stack as just another consumer: it imports
convert / product_layer / core_layer and never touches geometry itself. The
left rail visualises the descent (Domain -> Interoperability -> Core ->
Resource) and fills in with real counts once a file is inspected.

Run:
    python gui_qt.py [model.ifc]
"""

import os
import sys
import traceback
from pathlib import Path

from PySide6.QtCore import QObject, QThread, Qt, Signal
from PySide6.QtGui import QColor, QFont, QFontDatabase, QPainter
from PySide6.QtWidgets import (
    QAbstractItemView, QApplication, QCheckBox, QComboBox, QFileDialog,
    QFrame, QGridLayout, QHBoxLayout, QHeaderView, QLabel, QLineEdit,
    QMainWindow, QMessageBox, QProgressBar, QPushButton, QSizePolicy,
    QSplitter, QTextEdit, QTreeWidget, QTreeWidgetItem, QVBoxLayout, QWidget,
)

import ifcopenshell

import convert as converter
import core_layer
import product_layer

# --------------------------------------------------------------------------
# Design tokens.
#
# The four layer colours descend from warm to deep-cool, so the rail reads as
# literal depth: amber at the surface, violet at the bedrock where the
# coordinates live. They are the only saturated colour in the app; everything
# else is graphite on paper, so the descent stays the thing you look at.
# --------------------------------------------------------------------------

PAPER = "#FBFBFC"
PANEL = "#FFFFFF"
RULE = "#E3E5E9"
RULE_STRONG = "#CDD1D8"
INK = "#1E2126"
INK_MUTED = "#6B7280"
INK_FAINT = "#9AA1AC"
ACCENT = "#1D4ED8"

LAYER_COLOURS = {
    "Domain": "#D97706",
    "Interoperability": "#0F766E",
    "Core": "#1D4ED8",
    "Resource": "#5B21B6",
    "Unclassified": "#9AA1AC",
}

LAYER_BLURB = {
    "Domain": "Discipline-specific products",
    "Interoperability": "Shared building elements",
    "Core": "Placement and representation",
    "Resource": "Solids meshed",
}

# The rail counts different things per layer, because the layers hold
# different things. Domain / Interoperability / Core are counted in products.
# The Resource layer contains no products at all -- it holds the geometry
# primitives those products point down into -- so counting products there
# would print a misleading 0 for a file full of Resource entities.
LAYER_UNIT = {
    "Domain": "products",
    "Interoperability": "products",
    "Core": "products",
    "Resource": "items",
}

UNIT_NAMES = {
    0.001: "millimetres", 0.01: "centimetres", 1.0: "metres",
    0.0254: "inches", 0.3048: "feet", 0.9144: "yards",
}


def ui_font(size=13, weight=QFont.Normal):
    font = QFont()
    for family in ("Inter", "Segoe UI", "SF Pro Text", "Ubuntu", "Cantarell"):
        if family in QFontDatabase.families():
            font.setFamily(family)
            break
    font.setPointSize(size)
    font.setWeight(weight)
    return font


def mono_font(size=12):
    font = QFontDatabase.systemFont(QFontDatabase.FixedFont)
    for family in ("JetBrains Mono", "SF Mono", "Cascadia Mono", "Consolas",
                   "DejaVu Sans Mono"):
        if family in QFontDatabase.families():
            font.setFamily(family)
            break
    font.setPointSize(size)
    return font


STYLESHEET = f"""
QMainWindow, QWidget {{ background: {PAPER}; color: {INK}; }}
QFrame#Card {{
    background: {PANEL};
    border: 1px solid {RULE};
    border-radius: 6px;
}}
QFrame#Rail {{ background: {PANEL}; border-right: 1px solid {RULE}; }}
QLabel#Eyebrow {{ color: {INK_FAINT}; font-size: 10px; letter-spacing: 1.4px; }}
QLabel#Title {{ font-size: 19px; font-weight: 600; letter-spacing: -0.3px; }}
QLabel#SectionLabel {{ color: {INK_MUTED}; font-size: 11px; letter-spacing: 0.8px; }}
QLabel#Hint {{ color: {INK_FAINT}; font-size: 11px; }}
QLineEdit {{
    background: {PANEL};
    border: 1px solid {RULE_STRONG};
    border-radius: 5px;
    padding: 7px 9px;
    selection-background-color: {ACCENT};
}}
QLineEdit:focus {{ border: 1px solid {ACCENT}; }}
QComboBox {{
    background: {PANEL};
    border: 1px solid {RULE_STRONG};
    border-radius: 5px;
    padding: 6px 9px;
    min-width: 150px;
}}
QComboBox:focus {{ border: 1px solid {ACCENT}; }}
QComboBox::drop-down {{ border: none; width: 18px; }}
QPushButton {{
    background: {PANEL};
    border: 1px solid {RULE_STRONG};
    border-radius: 5px;
    padding: 7px 14px;
}}
QPushButton:hover {{ border-color: {INK_FAINT}; }}
QPushButton:disabled {{ color: {INK_FAINT}; background: {PAPER}; }}
QPushButton#Primary {{
    background: {ACCENT};
    border: 1px solid {ACCENT};
    color: #FFFFFF;
    font-weight: 600;
    padding: 9px 20px;
}}
QPushButton#Primary:hover {{ background: #1A45BE; }}
QPushButton#Primary:disabled {{
    background: {RULE_STRONG}; border-color: {RULE_STRONG}; color: #FFFFFF;
}}
QTreeWidget {{
    background: {PANEL};
    border: 1px solid {RULE};
    border-radius: 6px;
    outline: none;
}}
QTreeWidget::item {{ padding: 3px 2px; }}
QTreeWidget::item:selected {{ background: #E8EEFB; color: {INK}; }}
QHeaderView::section {{
    background: {PAPER};
    border: none;
    border-bottom: 1px solid {RULE};
    padding: 6px;
    color: {INK_MUTED};
    font-size: 11px;
}}
QTextEdit {{
    background: {PANEL};
    border: 1px solid {RULE};
    border-radius: 6px;
    padding: 8px;
}}
QProgressBar {{
    background: {RULE}; border: none; border-radius: 3px;
    height: 6px; text-align: center;
}}
QProgressBar::chunk {{ background: {ACCENT}; border-radius: 3px; }}
QCheckBox {{ spacing: 7px; }}
QSplitter::handle {{ background: {RULE}; width: 1px; }}
"""


# --------------------------------------------------------------------------
# Signature element: the descent rail.
# --------------------------------------------------------------------------

class LayerStep(QWidget):
    """One rung of the descent: marker, layer name, count, blurb."""

    def __init__(self, name, is_last=False):
        super().__init__()
        self.name = name
        self.colour = QColor(LAYER_COLOURS[name])
        self.is_last = is_last
        self._count = None
        self._active = False
        self.setMinimumHeight(66)
        self.setSizePolicy(QSizePolicy.Preferred, QSizePolicy.Fixed)

    def set_count(self, count):
        self._count = count
        self.update()

    def set_active(self, active):
        self._active = active
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)

        has_data = self._count is not None
        is_zero = has_data and self._count == 0
        dot_x, dot_y, radius = 18, 22, 5

        if not self.is_last:
            p.setPen(QColor(RULE_STRONG))
            p.drawLine(dot_x, dot_y + radius + 4, dot_x, self.height())

        colour = self.colour if has_data else QColor(RULE_STRONG)
        if is_zero:
            # Present but empty reads differently from not yet known: a hollow
            # ring in the layer's own colour rather than a filled marker.
            colour = QColor(self.colour)
            colour.setAlpha(90)
        if has_data and not is_zero:
            if self._active:
                halo = QColor(colour)
                halo.setAlpha(40)
                p.setPen(Qt.NoPen)
                p.setBrush(halo)
                p.drawEllipse(dot_x - radius - 5, dot_y - radius - 5,
                              (radius + 5) * 2, (radius + 5) * 2)
            p.setPen(Qt.NoPen)
            p.setBrush(colour)
            p.drawEllipse(dot_x - radius, dot_y - radius, radius * 2, radius * 2)
        else:
            p.setBrush(QColor(PANEL))
            p.setPen(colour if is_zero else QColor(RULE_STRONG))
            p.drawEllipse(dot_x - radius, dot_y - radius, radius * 2, radius * 2)

        text_x = dot_x + 18
        p.setPen(QColor(INK if has_data and not is_zero else INK_FAINT))
        p.setFont(ui_font(13, QFont.DemiBold))
        p.drawText(text_x, dot_y + 5, self.name)

        # Counts are monospaced so digits line up down the rail.
        p.setFont(mono_font(13))
        p.setPen(QColor(self.colour) if has_data and not is_zero
                 else QColor(INK_FAINT))
        p.drawText(self.rect().adjusted(0, dot_y - 10, -16, 0),
                   Qt.AlignRight | Qt.AlignTop,
                   str(self._count) if has_data else "\u2014")

        p.setFont(ui_font(10))
        p.setPen(QColor(INK_FAINT))
        blurb = LAYER_BLURB.get(self.name, "")
        if has_data:
            blurb = f"{blurb}  \u00b7  {LAYER_UNIT.get(self.name, '')}"
        p.drawText(text_x, dot_y + 22, blurb)
        p.end()


class DescentRail(QFrame):
    def __init__(self):
        super().__init__()
        self.setObjectName("Rail")
        self.setFixedWidth(252)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 20, 12, 18)
        layout.setSpacing(0)

        eyebrow = QLabel("REFERENCE DIRECTION")
        eyebrow.setObjectName("Eyebrow")
        layout.addWidget(eyebrow)
        layout.addSpacing(10)

        self.steps = {}
        names = product_layer.LAYER_ORDER
        for i, name in enumerate(names):
            step = LayerStep(name, is_last=(i == len(names) - 1))
            self.steps[name] = step
            layout.addWidget(step)

        layout.addSpacing(14)
        note = QLabel("References only ever point downward, so extracting "
                      "geometry is always a descent.")
        note.setObjectName("Hint")
        note.setWordWrap(True)
        layout.addWidget(note)
        layout.addStretch(1)

    def reset(self):
        for step in self.steps.values():
            step.set_count(None)
            step.set_active(False)

    def apply_counts(self, counts, resource_items=None):
        """Fill the rail. Resource is counted in meshed items, not products."""
        for name, step in self.steps.items():
            if name == "Resource":
                step.set_count(resource_items)
            else:
                step.set_count(counts.get(name, 0))

    def set_active(self, name):
        for key, step in self.steps.items():
            step.set_active(key == name)


# --------------------------------------------------------------------------
# Workers. Parsing and conversion both run off the UI thread, so a large
# model never freezes the window.
# --------------------------------------------------------------------------

class InspectWorker(QObject):
    done = Signal(object, object, float, str)
    failed = Signal(str)

    def __init__(self, path):
        super().__init__()
        self.path = path

    def run(self):
        try:
            model = ifcopenshell.open(self.path)
            selection = product_layer.select(model)
            scale = core_layer.unit_scale(model)
            self.done.emit(model, selection, scale, model.schema)
        except Exception:
            self.failed.emit(traceback.format_exc(limit=3))


class ConvertWorker(QObject):
    progress = Signal(int, int, str)
    done = Signal(object)
    failed = Signal(str)

    def __init__(self, kwargs):
        super().__init__()
        self.kwargs = kwargs
        self._cancel = False

    def cancel(self):
        self._cancel = True

    def run(self):
        try:
            report = converter.convert(
                progress=lambda i, t, p: self.progress.emit(i, t, p.is_a()),
                cancelled=lambda: self._cancel,
                **self.kwargs,
            )
            self.done.emit(report)
        except Exception:
            self.failed.emit(traceback.format_exc(limit=3))


# --------------------------------------------------------------------------
# Main window.
# --------------------------------------------------------------------------

def card(title=None):
    frame = QFrame()
    frame.setObjectName("Card")
    layout = QVBoxLayout(frame)
    layout.setContentsMargins(14, 12, 14, 14)
    layout.setSpacing(9)
    if title:
        label = QLabel(title.upper())
        label.setObjectName("SectionLabel")
        layout.addWidget(label)
    return frame, layout


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("IFC to OBJ")
        self.resize(1140, 780)
        self.setAcceptDrops(True)

        self.model = None
        self.selection = None
        self.thread = None
        self.worker = None

        root = QWidget()
        self.setCentralWidget(root)
        outer = QHBoxLayout(root)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        self.rail = DescentRail()
        outer.addWidget(self.rail)

        right = QWidget()
        outer.addWidget(right, 1)
        main = QVBoxLayout(right)
        main.setContentsMargins(20, 18, 20, 16)
        main.setSpacing(14)

        main.addLayout(self._build_header())
        main.addWidget(self._build_source_card())

        splitter = QSplitter(Qt.Horizontal)
        splitter.addWidget(self._build_contents_card())
        splitter.addWidget(self._build_output_card())
        splitter.setSizes([460, 400])
        main.addWidget(splitter, 1)

        main.addLayout(self._build_action_bar())

        self.setStyleSheet(STYLESHEET)
        self.setFont(ui_font())
        self._set_state("empty")

    # -- construction --------------------------------------------------

    def _build_header(self):
        column = QVBoxLayout()
        column.setSpacing(1)
        eyebrow = QLabel("IFC4 / IFC4X3  \u2192  WAVEFRONT OBJ")
        eyebrow.setObjectName("Eyebrow")
        title = QLabel("Layer descent converter")
        title.setObjectName("Title")
        column.addWidget(eyebrow)
        column.addWidget(title)
        return column

    def _build_source_card(self):
        frame, layout = card()
        grid = QGridLayout()
        grid.setHorizontalSpacing(9)
        grid.setVerticalSpacing(8)

        self.input_edit = QLineEdit()
        self.input_edit.setPlaceholderText("Choose an .ifc file, or drop one here")
        browse = QPushButton("Browse")
        browse.clicked.connect(self.choose_input)

        self.output_edit = QLineEdit()
        self.output_edit.setPlaceholderText("Set automatically from the source file")
        save_as = QPushButton("Change")
        save_as.clicked.connect(self.choose_output)

        grid.addWidget(QLabel("Source"), 0, 0)
        grid.addWidget(self.input_edit, 0, 1)
        grid.addWidget(browse, 0, 2)
        grid.addWidget(QLabel("Write to"), 1, 0)
        grid.addWidget(self.output_edit, 1, 1)
        grid.addWidget(save_as, 1, 2)
        grid.setColumnStretch(1, 1)
        layout.addLayout(grid)

        self.file_hint = QLabel("No file loaded.")
        self.file_hint.setObjectName("Hint")
        layout.addWidget(self.file_hint)
        return frame

    def _build_contents_card(self):
        frame, layout = card("What is in the file")
        self.tree = QTreeWidget()
        self.tree.setColumnCount(2)
        self.tree.setHeaderLabels(["Layer / schema / class", "Count"])
        self.tree.setSelectionMode(QAbstractItemView.SingleSelection)
        self.tree.header().setSectionResizeMode(0, QHeaderView.Stretch)
        self.tree.header().setSectionResizeMode(1, QHeaderView.ResizeToContents)
        self.tree.currentItemChanged.connect(self._tree_focus_changed)
        layout.addWidget(self.tree, 1)
        return frame

    def _build_output_card(self):
        frame, layout = card("Conversion")

        options = QGridLayout()
        options.setHorizontalSpacing(9)
        options.setVerticalSpacing(8)

        self.up_axis = QComboBox()
        self.up_axis.addItem("Z up  \u00b7  IFC native", "z")
        self.up_axis.addItem("Y up  \u00b7  most OBJ viewers", "y")

        self.layer_filter = QComboBox()
        self.layer_filter.addItem("All layers", None)
        for name in product_layer.LAYER_ORDER:
            self.layer_filter.addItem(name, name)

        options.addWidget(QLabel("Up axis"), 0, 0)
        options.addWidget(self.up_axis, 0, 1)
        options.addWidget(QLabel("Convert"), 1, 0)
        options.addWidget(self.layer_filter, 1, 1)
        options.setColumnStretch(1, 1)
        layout.addLayout(options)

        self.metres_check = QCheckBox("Rescale to metres using the file's length unit")
        self.metres_check.setChecked(True)
        layout.addWidget(self.metres_check)

        self.report = QTextEdit()
        self.report.setReadOnly(True)
        self.report.setFont(mono_font(11))
        self.report.setPlaceholderText("The conversion report appears here.")
        layout.addWidget(self.report, 1)
        return frame

    def _build_action_bar(self):
        row = QHBoxLayout()
        row.setSpacing(12)

        self.progress = QProgressBar()
        self.progress.setTextVisible(False)
        self.progress.setVisible(False)

        self.status = QLabel("")
        self.status.setObjectName("Hint")

        self.convert_button = QPushButton("Convert")
        self.convert_button.setObjectName("Primary")
        self.convert_button.clicked.connect(self.start_convert)

        row.addWidget(self.status, 1)
        row.addWidget(self.progress, 1)
        row.addWidget(self.convert_button)
        return row

    # -- state ---------------------------------------------------------

    def _set_state(self, state):
        self.state = state
        self.convert_button.setEnabled(state == "ready")

    # -- file handling -------------------------------------------------

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event):
        for url in event.mimeData().urls():
            path = url.toLocalFile()
            if path.lower().endswith(".ifc"):
                self.load(path)
                break

    def choose_input(self):
        path, _ = QFileDialog.getOpenFileName(
            self, "Choose an IFC file", "", "IFC files (*.ifc);;All files (*)")
        if path:
            self.load(path)

    def choose_output(self):
        path, _ = QFileDialog.getSaveFileName(
            self, "Write OBJ to", self.output_edit.text() or "",
            "OBJ files (*.obj)")
        if path:
            self.output_edit.setText(path)

    def load(self, path):
        self.input_edit.setText(path)
        p_path = Path(path)
        out_dir = p_path.parent / 'out'
        out_dir.mkdir(parents=True, exist_ok=True)
        out_path = out_dir / (p_path.stem + ".obj")
        self.output_edit.setText(str(out_path))
        self.report.clear()
        self.tree.clear()
        self.rail.reset()
        self._set_state("busy")
        self.status.setText("Reading the file...")
        self.file_hint.setText("Reading...")
        self.thread = QThread()
        self.worker = InspectWorker(path)
        self.worker.moveToThread(self.thread)
        self.thread.started.connect(self.worker.run)
        self.worker.done.connect(self.on_inspected)
        self.worker.failed.connect(self.on_failed)
        self.thread.start()

    def on_inspected(self, model, selection, scale, schema):
        self._stop_thread()
        self.model = model
        self.selection = selection

        self.rail.apply_counts(selection.by_layer())
        self._fill_tree(selection)

        unit = UNIT_NAMES.get(scale, f"units of {scale} m")
        self.file_hint.setText(
            f"{schema}  \u00b7  {len(selection)} products carrying geometry"
            f"  \u00b7  authored in {unit}"
        )
        self._set_state("ready")
        self.status.setText("Ready to convert.")

    def _fill_tree(self, selection):
        by_layer = {}
        for product, layer in selection:
            lname = layer.layer_name if layer else "Unclassified"
            sname = layer.layer_type if layer else "\u2014"
            cls = product.is_a()
            bucket = by_layer.setdefault(lname, {}).setdefault(sname, {})
            bucket[cls] = bucket.get(cls, 0) + 1

        for lname in product_layer.LAYER_ORDER + ["Unclassified"]:
            if lname not in by_layer:
                continue
            schemas = by_layer[lname]
            total = sum(sum(c.values()) for c in schemas.values())

            top = QTreeWidgetItem([lname, str(total)])
            top.setForeground(0, QColor(LAYER_COLOURS.get(lname, INK)))
            top.setFont(0, ui_font(12, QFont.DemiBold))
            top.setFont(1, mono_font(11))
            self.tree.addTopLevelItem(top)

            for sname in sorted(schemas):
                classes = schemas[sname]
                node = QTreeWidgetItem([sname, str(sum(classes.values()))])
                node.setFont(1, mono_font(11))
                node.setForeground(0, QColor(INK_MUTED))
                top.addChild(node)
                for cls in sorted(classes):
                    leaf = QTreeWidgetItem([cls, str(classes[cls])])
                    leaf.setFont(0, mono_font(11))
                    leaf.setFont(1, mono_font(11))
                    node.addChild(leaf)
            top.setExpanded(True)

    def _tree_focus_changed(self, current, _previous):
        if current is None:
            return
        item = current
        while item.parent():
            item = item.parent()
        self.rail.set_active(item.text(0))

    # -- conversion ----------------------------------------------------

    def start_convert(self):
        source = self.input_edit.text().strip()
        target = self.output_edit.text().strip()
        if not source or not target:
            QMessageBox.warning(self, "Missing path",
                                "Choose a source file and an output path.")
            return

        try:
            Path(target).parent.mkdir(parents=True, exist_ok=True)
        except Exception as e:
            QMessageBox.warning(self, "Directory Error", 
                                f"Could not create output directory:\n{e}")
            return

        layer = self.layer_filter.currentData()
        kwargs = dict(
            ifc_path=source,
            obj_path=target,
            layers={layer} if layer else None,
            to_metres=self.metres_check.isChecked(),
            up_axis=self.up_axis.currentData(),
        )

        self._set_state("busy")
        self.progress.setVisible(True)
        self.progress.setValue(0)
        self.report.clear()

        self.thread = QThread()
        self.worker = ConvertWorker(kwargs)
        self.worker.moveToThread(self.thread)
        self.thread.started.connect(self.worker.run)
        self.worker.progress.connect(self.on_progress)
        self.worker.done.connect(self.on_converted)
        self.worker.failed.connect(self.on_failed)
        self.thread.start()

    def on_progress(self, index, total, class_name):
        self.progress.setMaximum(max(total, 1))
        self.progress.setValue(index + 1)
        self.status.setText(f"{class_name}   {index + 1} of {total}")

    def on_converted(self, report):
        self._stop_thread()
        self.progress.setVisible(False)
        self.report.setPlainText(report.render())
        self.rail.apply_counts(report.layer_counts, report.items_built)
        self.status.setText(
            f"Wrote {report.triangles} triangles from {report.converted} products."
        )
        self._set_state("ready")

    def on_failed(self, message):
        self._stop_thread()
        self.progress.setVisible(False)
        self.file_hint.setText("That file could not be read.")
        self.report.setPlainText(message)
        self.status.setText("Conversion stopped.")
        self._set_state("ready" if self.model else "empty")

    def _stop_thread(self):
        if self.thread is not None:
            self.thread.quit()
            self.thread.wait(3000)
            self.thread = None
            self.worker = None

    def closeEvent(self, event):
        if self.worker is not None and hasattr(self.worker, "cancel"):
            self.worker.cancel()
        self._stop_thread()
        event.accept()


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("IFC to OBJ")
    window = MainWindow()
    if len(sys.argv) > 1 and os.path.isfile(sys.argv[1]):
        window.load(sys.argv[1])
    window.show()
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
