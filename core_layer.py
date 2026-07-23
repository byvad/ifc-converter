"""Core layer: the hinge of the descent.

An IfcProduct (Kernel schema) is where "this is a thing in the world" is
defined. It carries exactly the two attributes a converter needs, and both of
them point straight down into the Resource layer:

    ObjectPlacement -> IfcLocalPlacement   (Geometric Constraint, Resource)
    Representation  -> IfcProductDefinitionShape (Representation, Resource)

This module does not compute any geometry itself. It resolves those two
references, hands the representation items to the Resource layer, and applies
the placement to whatever comes back. That division is the whole point: Core
knows about *products*, Resource knows about *shapes*, and neither needs the
other's vocabulary.

Also handled here: IfcRelVoidsElement, a Product-schema relationship that
attaches openings to their host element.
"""

from resource_layer import (
    Mesh,
    UnsupportedGeometry,
    build_item,
    local_placement_matrix,
)

# Representation identifiers worth meshing. 'Body' is the solid geometry;
# 'Axis', 'FootPrint' and 'Box' are simplified stand-ins that would draw lines
# and bounding boxes into the OBJ if included.
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
    """Does this product carry a shape representation at all?

    Spatial elements (IfcSite, IfcBuildingStorey) are products but usually
    carry no body geometry, and type objects carry none by definition.
    """
    rep = getattr(product, "Representation", None)
    return rep is not None and getattr(rep, "Representations", None)


def resolve(product, include_openings=False):
    """Descend from a Core-layer product to a placed mesh.

    The order matters and mirrors the layer hierarchy:
      1. Core reads Representation and ObjectPlacement (both downward refs).
      2. Resource turns each representation item into local-space triangles.
      3. Core applies the placement matrix, lifting local -> world.
    """
    result = ProductGeometry(product)

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
                result.mesh.extend(build_item(item))
                result.items_built += 1
            except UnsupportedGeometry as exc:
                result.unsupported.append(str(exc))
            except (NotImplementedError, ValueError, AttributeError, IndexError) as exc:
                result.unsupported.append(f"{item.is_a()}: {exc}")

    # Local space -> world space. This is the step 'use-world-coords' performs
    # for you in ifcopenshell.geom.
    placement = local_placement_matrix(getattr(product, "ObjectPlacement", None))
    result.mesh = result.mesh.transformed(placement)

    return result


def openings_of(product):
    """Yield IfcOpeningElement instances voiding this product.

    IfcRelVoidsElement is a Product-schema (Core) relationship. The opening's
    geometry is the *void volume*, not material, which is exactly why exporting
    openings as solids fills every doorway with a block.
    """
    for rel in getattr(product, "HasOpenings", None) or []:
        if rel.is_a("IfcRelVoidsElement"):
            yield rel.RelatedOpeningElement


def unit_scale(model):
    """Metres per model length unit.

    IFC files are commonly authored in millimetres. Nothing in the geometry
    entities records this: the scale lives in the project's IfcUnitAssignment,
    a Measure-schema (Resource) structure. Miss it and your building comes out
    a thousand times too big.

    Two forms of length unit exist and both must be handled:

      IfcSIUnit               metre, optionally with a prefix (MILLI, CENTI)
      IfcConversionBasedUnit  inch, foot, yard, mile - a named unit carrying an
                              IfcMeasureWithUnit that converts it back to SI

    Handling only the SI case silently returns 1.0 for any imperial file, and
    imperial IFC is common in North American practice. An inch file then comes
    out 39.37x too large, which reads as a mystery scaling bug rather than a
    unit bug.
    """
    for project in model.by_type("IfcProject"):
        assignment = getattr(project, "UnitsInContext", None)
        if assignment is None:
            continue
        for unit in assignment.Units:
            if getattr(unit, "UnitType", None) != "LENGTHUNIT":
                continue
            scale = _length_unit_scale(unit)
            if scale is not None:
                return scale
    return 1.0


_SI_PREFIXES = {
    "EXA": 1e18, "PETA": 1e15, "TERA": 1e12, "GIGA": 1e9, "MEGA": 1e6,
    "KILO": 1e3, "HECTO": 1e2, "DECA": 1e1, "DECI": 1e-1, "CENTI": 1e-2,
    "MILLI": 1e-3, "MICRO": 1e-6, "NANO": 1e-9, "PICO": 1e-12,
}


def _length_unit_scale(unit):
    """Metres per one of this unit, or None if it is not a length unit."""
    if unit.is_a("IfcSIUnit"):
        prefix = getattr(unit, "Prefix", None)
        return _SI_PREFIXES.get(prefix, 1.0) if prefix else 1.0

    if unit.is_a("IfcConversionBasedUnit"):
        # ConversionFactor is an IfcMeasureWithUnit: a numeric value paired
        # with the unit that value is expressed in. For 'inch' that is
        # IfcLengthMeasure(0.0254) against IfcSIUnit(METRE).
        factor = getattr(unit, "ConversionFactor", None)
        if factor is None:
            return None
        value = factor.ValueComponent
        # ifcopenshell hands back either a bare float or a wrapped measure.
        value = float(getattr(value, "wrappedValue", value))
        base = _length_unit_scale(factor.UnitComponent)
        if base is None:
            return None
        return value * base

    return None
