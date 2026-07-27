# @author: Davy Bellens

"""Product schema: resolving spatial placement and representation."""

from conversion.layers.core.openings import openings_of
from conversion.layers.resource import (
    Mesh,
    boolean,
    UnsupportedGeometry,
    build_item,
    local_placement_matrix,
)

MESHABLE_IDENTIFIERS = {"Body", "Facetation", None}

SKIPPED_IDENTIFIERS = {"Axis", "FootPrint", "Profile", "Box", "Annotation",
                       "SurveyPoints", "Reference", "Clearance", "Lighting"}


class ProductGeometry:
    """The result of descending through one product."""

    def __init__(self, product):
        self.product = product
        self.mesh = Mesh()
        self.unsupported = []
        self.skipped_representations = []
        self.items_built = 0
        self.styled_before_material = 0
        self.styled_by_material = False
        self.openings_cut = 0

    @property
    def styled(self):
        return bool(self.mesh.groups)

    @property
    def guid(self):
        return getattr(self.product, "GlobalId", None)

    @property
    def name(self):
        return getattr(self.product, "Name", None)

    @property
    def has_geometry(self):
        return len(self.mesh.triangles) > 0


def has_shape(product):
    """Does this product carry a shape representation at all?"""
    rep = getattr(product, "Representation", None)
    return rep is not None and getattr(rep, "Representations", None)


def resolve(product, include_openings=True, palette=None):
    """Descend from a Core-layer product to a placed, styled mesh."""
    result = ProductGeometry(product)
    styles = palette is not None

    if not has_shape(product):
        return result

    for representation in product.Representation.Representations:
        identifier = getattr(representation, "RepresentationIdentifier", None)

        if identifier in SKIPPED_IDENTIFIERS:
            result.skipped_representations.append(identifier)
            continue
        if identifier not in MESHABLE_IDENTIFIERS:
            result.skipped_representations.append(identifier)
            continue

        for item in representation.Items:
            try:
                result.mesh.extend(build_item(item, styles))
                result.items_built += 1
            except UnsupportedGeometry as exc:
                result.unsupported.append(str(exc))
            except (NotImplementedError, ValueError, AttributeError, IndexError) as exc:
                result.unsupported.append(f"{item.is_a()}: {exc}")

    placement = local_placement_matrix(getattr(product, "ObjectPlacement", None))
    result.mesh = result.mesh.transformed(placement)

    if include_openings:
        cutters = []
        for opening in openings_of(product):
            void = resolve(opening, include_openings=False, palette=None)
            if void.has_geometry:
                cutters.append(void.mesh)
        if cutters:
            result.mesh = boolean.subtract(result.mesh, cutters)
            result.openings_cut = len(cutters)

    if palette is not None:
        result.styled_before_material = len(result.mesh.groups)
        result.mesh.fill_style(palette.product_rgba(product))
        result.styled_by_material = (
            len(result.mesh.groups) > result.styled_before_material
        )

    return result