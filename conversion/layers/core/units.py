# @author: Davy Bellens

"""Measure schema logic: identifying scale factors from the IFC project."""

_SI_PREFIXES = {
    "EXA": 1e18, "PETA": 1e15, "TERA": 1e12, "GIGA": 1e9, "MEGA": 1e6,
    "KILO": 1e3, "HECTO": 1e2, "DECA": 1e1, "DECI": 1e-1, "CENTI": 1e-2,
    "MILLI": 1e-3, "MICRO": 1e-6, "NANO": 1e-9, "PICO": 1e-12,
}


def unit_scale(model):
    """Metres per model length unit.

    IFC files are commonly authored in millimetres. Nothing in the geometry
    entities records this: the scale lives in the project's IfcUnitAssignment,
    a Measure-schema (Resource) structure.
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


def _length_unit_scale(unit):
    """Metres per one of this unit, or None if it is not a length unit."""
    if unit.is_a("IfcSIUnit"):
        prefix = getattr(unit, "Prefix", None)
        return _SI_PREFIXES.get(prefix, 1.0) if prefix else 1.0

    if unit.is_a("IfcConversionBasedUnit"):
        factor = getattr(unit, "ConversionFactor", None)
        if factor is None:
            return None
        value = factor.ValueComponent
        value = float(getattr(value, "wrappedValue", value))
        base = _length_unit_scale(factor.UnitComponent)
        if base is None:
            return None
        return value * base

    return None