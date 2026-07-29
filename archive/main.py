import sys
from PySide6.QtGui import QGuiApplication
from archive.gui import AppWindow

def main():
    app = QGuiApplication(sys.argv)
    _ = AppWindow()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()