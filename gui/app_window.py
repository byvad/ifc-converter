import os
import sys
from pathlib import Path
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuickControls2 import QQuickStyle


from gui.bridge import Bridge

class AppWindow:
    def __init__(self):
        self.engine = QQmlApplicationEngine()
        QQuickStyle.setStyle("Basic")
        
        # 1. Register the qml folder so the Qt Engine finds qmldir and Style.qml
        qml_path = Path(__file__).parent / "qml"
        self.engine.addImportPath(os.fspath(qml_path))
        
        # 2. Initialize the Python-QML Bridge
        self.bridge = Bridge(self.engine)
        
        # 3. Load the main user interface
        main_qml_file = qml_path / "main.qml"
        self.engine.load(os.fspath(main_qml_file))
        
        # Prevent silent failure if the QML file contains syntax errors
        if not self.engine.rootObjects():
            sys.exit(-1)
            
        # 4. Wire the QML signals directly to the Bridge slots
        root = self.engine.rootObjects()[0]
        root.fileDropped.connect(self.bridge.handle_file_dropped)
        root.convertRequested.connect(self.bridge.handle_convert)