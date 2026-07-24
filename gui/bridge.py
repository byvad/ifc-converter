import os
import re
from urllib.parse import unquote

from PySide6.QtCore import QObject, Slot, QThread, QUrl, QDir

from gui.workers import InspectWorker, ConvertWorker
from conversion.convert import output_obj_path

# Descent order for the tree. If the taxonomy package exports this, import
# it instead of keeping a second copy here.
LAYER_ORDER = ["Domain", "Interoperability", "Core", "Resource"]


def _tree_rows(selection):
    """Flatten layer -> schema -> class into depth-tagged rows."""
    grouped = {}
    for product, layer in selection:
        lname = layer.layer_name if layer else "Unclassified"
        sname = layer.layer_type if layer else "\u2014"
        classes = grouped.setdefault(lname, {}).setdefault(sname, {})
        classes[product.is_a()] = classes.get(product.is_a(), 0) + 1

    rows = []
    for lname in LAYER_ORDER + ["Unclassified"]:
        schemas = grouped.get(lname)
        if not schemas:
            continue
        total = sum(sum(c.values()) for c in schemas.values())
        rows.append({"depth": 0, "label": lname, "count": total, "layer": lname})
        for sname in sorted(schemas):
            classes = schemas[sname]
            rows.append({"depth": 1, "label": sname,
                         "count": sum(classes.values()), "layer": lname})
            for cls in sorted(classes):
                rows.append({"depth": 2, "label": cls,
                             "count": classes[cls], "layer": lname})
    return rows

def _local_path(value):
    """Turn whatever QML hands over into a real OS path.

    Handles: QUrl, a full 'file:///C:/...' URL, and a URL whose scheme was
    already stripped on the QML side leaving '/C:/...' or '\\C:\\...'.
    """
    if isinstance(value, QUrl):
        return QDir.toNativeSeparators(value.toLocalFile())

    if not isinstance(value, str):
        return value

    text = value.strip()
    if not text:
        return text

    if text.startswith("file:"):
        return QDir.toNativeSeparators(QUrl(text).toLocalFile())

    # Leading separator in front of a drive letter: '/C:/...' or '\C:\...'
    if re.match(r"^[/\\][A-Za-z]:", text):
        text = text[1:]

    if "%" in text:
        text = unquote(text)

    return QDir.toNativeSeparators(text)


class Bridge(QObject):
    def __init__(self, engine):
        super().__init__()
        self.engine = engine

        # Keep references to prevent garbage collection while running
        self.inspect_thread = None
        self.inspect_worker = None
        self.convert_thread = None
        self.convert_worker = None

    # ------------------------------------------------------------------ utils

    def _root(self):
        objects = self.engine.rootObjects()
        return objects[0] if objects else None

    def _set(self, object_name, prop, value):
        root = self._root()
        if root is None:
            return
        item = root.findChild(QObject, object_name)
        if item is not None:
            item.setProperty(prop, value)

    def _get(self, object_name, prop):
        root = self._root()
        if root is None:
            return None
        item = root.findChild(QObject, object_name)
        return item.property(prop) if item is not None else None

    # -- progress ------------------------------------------------------------
    #
    # Two shapes of wait. Parsing and the pre-mesh setup are single opaque
    # calls with nothing to count, so they get an indeterminate bar; a bar
    # that filled would be claiming knowledge the code does not have. Only
    # the per-product loop reports a real fraction.

    def _busy(self, message):
        self._set("statusLabel", "text", message)
        self._set("progressBar", "indeterminate", True)
        self._set("progressBar", "visible", True)

    def _idle(self, message=None):
        self._set("progressBar", "visible", False)
        self._set("progressBar", "indeterminate", False)
        self._set("progressBar", "value", 0)
        if message is not None:
            self._set("statusLabel", "text", message)

    # ---------------------------------------------------------------- inspect

    @Slot(str)
    def handle_file_dropped(self, path):
        if self.inspect_thread is not None and self.inspect_thread.isRunning():
            return

        path = _local_path(path)
        self._set("sourceField", "text", path)

        if not os.path.isfile(path):
            self.on_inspect_failed(f"File not found: {path}")
            return

        # Pure string maths on the filename: no reason to make this wait on
        # the parse, so both fields fill on the same frame as the drop.
        self._set("targetField", "text", output_obj_path(path))
        self._busy(f"Reading {os.path.basename(path)}\u2026")
        self._set("contentsTree", "model", [])

        self.inspect_thread = QThread()
        self.inspect_worker = InspectWorker(path)
        self.inspect_worker.moveToThread(self.inspect_thread)

        self.inspect_thread.started.connect(self.inspect_worker.run)
        self.inspect_worker.done.connect(self.on_inspect_done)
        self.inspect_worker.failed.connect(self.on_inspect_failed)

        self.inspect_worker.done.connect(self.inspect_thread.quit)
        self.inspect_worker.failed.connect(self.inspect_thread.quit)
        self.inspect_worker.done.connect(self.inspect_worker.deleteLater)
        self.inspect_worker.failed.connect(self.inspect_worker.deleteLater)
        self.inspect_thread.finished.connect(self.inspect_thread.deleteLater)
        self.inspect_thread.finished.connect(self._clear_inspect)

        self.inspect_thread.start()

    def _clear_inspect(self):
        # Drop the Python references once the C++ objects are scheduled for
        # deletion, otherwise the next isRunning() check hits a dead object.
        self.inspect_thread = None
        self.inspect_worker = None

    @Slot(object, object, float, str)
    def on_inspect_done(self, model, selection, scale, schema):
        self._idle()

        counts = selection.by_layer() if hasattr(selection, "by_layer") else {}
        # Products are never Resource-layer entities; the key is shown so the
        # sidebar always renders all four rungs.
        counts.setdefault("Resource", 0)
        self._set("sidebar", "layerCounts", counts)
        self._set("contentsTree", "model", _tree_rows(selection))

        unclassified = getattr(selection, "unclassified", [])
        if unclassified:
            preview = ", ".join(unclassified[:5])
            more = f" (+{len(unclassified) - 5})" if len(unclassified) > 5 else ""
            self._set("statusLabel", "text", f"Unclassified: {preview}{more}")
        else:
            self._set("statusLabel", "text", f"{schema} \u2014 {len(selection)} products")

    @Slot(str)
    def on_inspect_failed(self, error):
        self._idle("Inspection failed.")
        print(f"Inspection Failed:\n{error}")

    # ---------------------------------------------------------------- convert

    @Slot(str, str, str, str, bool, bool, bool)
    def handle_convert(self, source, target, up_axis, layer, metres, colors, glass):
        if self.convert_thread is not None and self.convert_thread.isRunning():
            return

        source = _local_path(source)
        target = _local_path(target)

        if not os.path.isfile(source):
            self.on_convert_failed(f"File not found: {source}")
            return

        kwargs = {
            "source": source,
            "target": target,
            "up_axis": up_axis,
            "layer": layer,
            "metres": metres,
            "colors": colors,
            "glass": glass,
        }

        # The palette build and the product selection both run before the
        # first product is meshed, so without this the bar would sit dead
        # for the first few seconds of a large model.
        self._busy("Preparing\u2026")

        self.convert_thread = QThread()
        self.convert_worker = ConvertWorker(kwargs)
        self.convert_worker.moveToThread(self.convert_thread)

        self.convert_thread.started.connect(self.convert_worker.run)
        self.convert_worker.progress.connect(self.on_convert_progress)
        self.convert_worker.done.connect(self.on_convert_done)
        self.convert_worker.failed.connect(self.on_convert_failed)

        self.convert_worker.done.connect(self.convert_thread.quit)
        self.convert_worker.failed.connect(self.convert_thread.quit)
        self.convert_worker.done.connect(self.convert_worker.deleteLater)
        self.convert_worker.failed.connect(self.convert_worker.deleteLater)
        self.convert_thread.finished.connect(self.convert_thread.deleteLater)
        self.convert_thread.finished.connect(self._clear_convert)

        self.convert_thread.start()

    def _clear_convert(self):
        self.convert_thread = None
        self.convert_worker = None

    @Slot(int, int, str)
    def on_convert_progress(self, current, total, name):
        if total > 0:
            self._set("progressBar", "indeterminate", False)
            self._set("progressBar", "visible", True)
            self._set("progressBar", "value", current / total)
        self._set("statusLabel", "text", f"Converting: {name}")

    @Slot(object)
    def on_convert_done(self, report):
        self._idle("Done.")
        self._set("reportArea", "text", report.render())

    @Slot(str)
    def on_convert_failed(self, error):
        self._idle("Conversion failed.")
        print(f"Conversion Failed:\n{error}")