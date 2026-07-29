# @author: Davy Bellens

import os
import sys
from pathlib import Path
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuickControls2 import QQuickStyle

from archive.gui.bridge import Bridge

QML_DIR = Path(__file__).parent / "qml"
MAIN_QML = QML_DIR / "main.qml"

class AppWindow:
    def __init__(self):
        self._init_engine()
        self._register_qml_dir()
        self.bridge = Bridge(self.engine)
        self._load_qml()
        self._wire_signals()

    def _init_engine(self):
        """ Initialize the Qt QML engine and set the style for the application. """
        self.engine = QQmlApplicationEngine()
        QQuickStyle.setStyle("Basic")

    def _register_qml_dir(self):
        """ Register the QML directory with the engine so that it can find the QML files and resources. """
        self.engine.addImportPath(os.fspath(QML_DIR))

    def _load_qml(self):
        """ Load the main QML file into the engine. """
        self.engine.load(os.fspath(MAIN_QML))
        
        if not self.engine.rootObjects():
            sys.exit(-1)

    def _wire_signals(self):
        """ Wire the QML signals directly to the Bridge slots. """
        root = self.engine.rootObjects()[0]
        root.fileDropped.connect(self.bridge.handle_file_dropped)
        root.convertRequested.connect(self.bridge.handle_convert)