
DL_SCHEMAS = {
    "Building Controls", "Plumbing FireProtections", "Structural Elements",
    "Structural Analysis", "HVAC", "Electrical", "Architecture",
    "Construction Management",
}
IO_SCHEMAS = {
    "Shared Bldg Services", "Shared Components", "Shared Building",
    "Shared Management", "Shared Facilities",
}
CORE_SCHEMAS = {"Control", "Product", "Process", "Kernel"}
RSRC_SCHEMAS = {
    "DateTime", "Material", "External Reference", "Geometric Constraint",
    "Geometric Model", "Geometry", "Actor", "Profile", "Property", "Quantity",
    "Topology", "Utility", "Measure", "Presentation Appearance",
    "Presentation Definition", "Presentation Organization", "Representation",
    "Constraint", "Approval", "Structural Load", "Cost",
}


class Layer:
    """A conceptual layer, holding the set of schemas legal within it."""

    def __init__(self, layer_name, types, layer_type):
        self._layer_name = layer_name
        self._layer_types = types
        self.layer_type = layer_type

    @property
    def layer_name(self):
        return self._layer_name

    @property
    def layer_type(self):
        return self._layer_type

    @layer_type.setter
    def layer_type(self, value):
        if value not in self._layer_types:
            raise ValueError(f"Invalid {self._layer_name} Layer Schema: {value!r}")
        self._layer_type = value

    def __repr__(self):
        return f"{type(self).__name__}({self._layer_type!r})"


class Domain(Layer):
    def __init__(self, domain_type):
        super().__init__("Domain", DL_SCHEMAS, domain_type)


class InterOperability(Layer):
    def __init__(self, io_type):
        super().__init__("Interoperability", IO_SCHEMAS, io_type)


class Core(Layer):
    def __init__(self, core_type):
        super().__init__("Core", CORE_SCHEMAS, core_type)


class Resource(Layer):
    def __init__(self, resource_type):
        super().__init__("Resource", RSRC_SCHEMAS, resource_type)

