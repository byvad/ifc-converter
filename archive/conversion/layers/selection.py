# @author: Davy Bellens

"""
Domain and Interoperability layers: where the descent starts.

These are the top two layers, and between them they hold every entity a person
would call "a thing in the building":

    Interoperability   IfcWall, IfcSlab, IfcDoor, IfcWindow, IfcFurniture
                       - shared across disciplines
    Domain             IfcAirTerminal, IfcLightFixture, IfcPile
                       - specific to one discipline

Neither layer knows anything about geometry. Their job here is selection: work
out which products to convert, and which conceptual schema each belongs to.
The moment a product is chosen, the descent hands off to the Core layer.
"""

from archive.conversion.ifc.classification import Domain, InterOperability, classify_instance

# Openings carry real geometry (the void volume) but must not be drawn, and
# spaces are volumes of air. Both are Core/Product-schema entities rather than
# Domain or Interoperability products, but they turn up in by_type sweeps.
NON_VISUAL = {"IfcOpeningElement", "IfcSpace", "IfcVoidingFeature",
              "IfcGrid", "IfcAnnotation"}


class ProductSelection:
    """Products chosen for conversion, grouped by conceptual layer."""

    def __init__(self):
        self.entries = []       # (product, Layer or None)
        self.unclassified = []  # entity names with no layer mapping

    def add(self, product, layer):
        self.entries.append((product, layer))
        if layer is None:
            name = product.is_a()
            if name not in self.unclassified:
                self.unclassified.append(name)

    def __iter__(self):
        return iter(self.entries)

    def __len__(self):
        return len(self.entries)

    def by_layer(self):
        """Group counts by layer name, for reporting."""
        counts = {}
        for _, layer in self.entries:
            key = layer.layer_name if layer else "Unclassified"
            counts[key] = counts.get(key, 0) + 1
        return counts

    def by_schema(self):
        counts = {}
        for _, layer in self.entries:
            key = f"{layer.layer_name}/{layer.layer_type}" if layer else "Unclassified"
            counts[key] = counts.get(key, 0) + 1
        return counts


def select(model, layers=None, schemas=None, classes=None):
    """Choose which products to convert.

    layers   restrict to conceptual layers, e.g. {"Domain"}
    schemas  restrict to schemas, e.g. {"HVAC", "Shared Building"}
    classes  restrict to entity names, e.g. {"IfcWall"}

    With no filters, every IfcProduct carrying a shape is selected. Note that
    filtering by layer is only meaningful because of the dependency rule: an
    entity's layer is fixed by the schema, not by the file.
    """
    selection = ProductSelection()

    for product in model.by_type("IfcProduct"):
        name = product.is_a()

        if name in NON_VISUAL:
            continue
        if classes and name not in classes:
            continue

        layer = classify_instance(product)

        if layers:
            if layer is None or layer.layer_name not in layers:
                continue
        if schemas:
            if layer is None or layer.layer_type not in schemas:
                continue

        selection.add(product, layer)

    return selection


def describe(product):
    """Human-readable 'IfcWall [Interoperability/Shared Building]'."""
    layer = classify_instance(product)
    if layer is None:
        return f"{product.is_a()} [unclassified]"
    return f"{product.is_a()} [{layer.layer_name}/{layer.layer_type}]"


def discipline_products(model):
    """Only Domain-layer products: the discipline-specific equipment."""
    return select(model, layers={"Domain"})


def shared_products(model):
    """Only Interoperability-layer products: the shared building elements."""
    return select(model, layers={"Interoperability"})


LAYER_ORDER = ["Domain", "Interoperability", "Core", "Resource"]


def layer_index(layer):
    """Position in the hierarchy. Lower index = higher layer."""
    if layer is None:
        return len(LAYER_ORDER)
    return LAYER_ORDER.index(layer.layer_name)


__all__ = [
    "Domain", "InterOperability", "ProductSelection", "select", "describe",
    "discipline_products", "shared_products", "layer_index", "LAYER_ORDER",
]