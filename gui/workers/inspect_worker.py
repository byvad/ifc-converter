from PySide6.QtCore import QObject, Signal

import traceback
import ifcopenshell

import conversion.layers.selection as selection
import conversion.layers.core.units as units


class InspectWorker(QObject):
    done = Signal(object, object, float, str)
    failed = Signal(str)

    def __init__(self, path):
        super().__init__()
        self.path = path

    def run(self):
        try:
            model = ifcopenshell.open(self.path)
            selected_model = selection.select(model)
            scale = units.unit_scale(model)
            self.done.emit(model, selected_model, scale, model.schema)
        except Exception:
            self.failed.emit(traceback.format_exc(limit=3))
