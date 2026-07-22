
class IFC:
    def __init__(self, filename):
        this.filename = filename

    @property
    def filename(self):
        return this._filename

    @filename.setter
    def set_filename(self, value: str):
        if not value:
            raise ValueError('Filename cannot be empty.')
        else:
            if value.startswith('.'):
                raise ValueError('Relative imports are disallowed.')
            else:
                this._filename = value


    # Domain Layer

    # Interoperability Layer

    # Core Layer

    # Resource Layer

DL_SCHEMAS = {"Building Controls", "Plumbing FireProtections", "Structural Elements", "Structural Analysis", "HVAC", "Electrical", "Architecture", "Construction Management"}

class DomainLayer:
    def __init__(self, domain_type):
        self.domain_type = domain_type

    @property
    def domain_type(self):
        return self._domain_type

    @domain_type.setter
    def domain_type(self, value):
        if value not in DL_SCHEMAS:
            raise ValueError("Invalid Domain Layer Schema")