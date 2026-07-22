class IFC:
    def __init__(self, filename):
        this.filename = filename

    @property
    def filename(self):
        return self._filename

    @filename.setter
    def set_filename(self, value: str):
        if not value:
            raise ValueError('Filename cannot be empty.')
        else:
            if value.startswith('.'):
                raise ValueError('Relative imports are disallowed.')
            else:
                this._filename = value


DL_SCHEMAS = {"Building Controls", "Plumbing FireProtections", "Structural Elements", "Structural Analysis", "HVAC", "Electrical", "Architecture", "Construction Management"}
IO_SCHEMAS = {"Shared Bldg Services", "Shared Components", "Shared Building", "Shared Management", "Shared Facilities"}
CONTROL_SCHEMAS = {"Control", "Product", "Process", "Kernel"}
RSRC_SCHEMAS = {"DateTime", "Material", "External Reference", "Geometric Constraint", "Geometric Model", "Geometry", "Actor", "Profile", "Property", "Quantity", "Topology", "Utility", "Measure", "Presentation Appearance", "Presentation Definition", "Presentation Organization", "Representation", "Constraint", "Approval", "Structural Load", "Cost"}

class Layer:
    def __init__(self, types, layer_type):
        self._layer_types = types
        self.layer_type = layer_type

    @property
    def layer_type(self):
        return self._layer_type

    @layer_type.setter
    def layer_type(self, value):
        if value not in self._layer_types:
            raise ValueError("Invalid Domain Layer Schema")

# Domain Layer
class Domain(Layer):
    def __init__(self, domain_type):
        super().__init__(DL_SCHEMAS, domain_type)

# Interoperability Layer
class InterOperanility(Layer):
    def __init__(self, io_type):
        super().__init__(IO_SCHEMAS, io_type)

# Core Layer
class Control(Layer):
    def __init__(self, control_type):
        super().__init__(CONTROL_SCHEMAS, control_type)

# Resource Layer
class Resource(Layer):
    def __init__(self, resource_type):
        super().__init__(RSRC_SCHEMAS, resource_type)
