class IFC:
    def __init__(self, filename):
        self.filename = filename

    @property
    def filename(self):
        return self._filename

    @filename.setter
    def filename(self, value: str):
        if not value:
            raise ValueError('Filename cannot be empty.')
        if value.startswith('.'):
            raise ValueError('Filename cannot start with "." (hidden file or relative path).')
        self._filename = value