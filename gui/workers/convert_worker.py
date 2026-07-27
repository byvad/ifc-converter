# @author: Davy Bellens

import traceback
from PySide6.QtCore import QObject, Signal

from conversion.convert import convert

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
        k = self.kwargs
        try:
            report = convert(
                k["source"],
                k["target"],
                layers={k["layer"]} if k["layer"] else None,
                to_metres=k["metres"],
                up_axis=k["up_axis"],
                colour=k["colors"],
                min_alpha=0.25 if (k["colors"] and k["glass"]) else 0.0,
                progress=lambda i, t, p: self.progress.emit(i, t, p.is_a()),
                cancelled=lambda: self._cancel,
            )
            self.done.emit(report)
        except Exception:
            self.failed.emit(traceback.format_exc(limit=3))

