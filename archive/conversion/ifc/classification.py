# @author: Davy Bellens

"""The IFC conceptual layer taxonomy.

The four layers form a dependency hierarchy, not a data flow. An entity in a given
layer may reference entities in its own layer or any layer below it, but never
above. Domain sits on top; Resource sits at the bottom.

    Domain            IfcAirTerminal, IfcLightFixture, IfcPile
    Interoperability  IfcWall, IfcSlab, IfcDoor, IfcFurniture
    Core              IfcProduct, IfcRelAggregates, IfcBuildingStorey
    Resource          IfcCartesianPoint, IfcExtrudedAreaSolid, IfcPolyline

Because references only point downward, resolving a product's geometry is
always a descent: start at a Domain or Interoperability product, pass through
Core to reach its placement and representation, and bottom out in Resource
where the actual coordinates live.
"""

import json
from pathlib import Path

from .taxonomy import Core, Domain, InterOperability, Resource

_ENTITY_SCHEMA = {}


def _load_schemas():
    """Load taxonomy mappings from the JSON schema directory."""
    schema_dir = Path(__file__).parent / "schemas"
    layer_classes = {
        "Domain": Domain,
        "InterOperability": InterOperability,
        "Core": Core,
        "Resource": Resource
    }
    
    for json_file in schema_dir.glob("*.json"):
        with open(json_file, "r", encoding="utf-8") as f:
            data = json.load(f)
            layer_name = data.get("layer")
            layer_cls = layer_classes.get(layer_name)
            
            if not layer_cls:
                continue
                
            for schema, entities in data.get("schemas", {}).items():
                for entity in entities:
                    _ENTITY_SCHEMA[entity.upper()] = (schema, layer_cls)

# Execute on import to populate _ENTITY_SCHEMA
_load_schemas()


def classify(entity_name):
    """Return the Layer instance for an IFC entity name, or None.

    Falling back through the IFC inheritance chain is deliberately avoided here: 
    an unknown entity should announce itself rather than be silently filed under 
    its parent's layer.
    """
    hit = _ENTITY_SCHEMA.get(entity_name.upper())
    if hit is None:
        return None
    schema, layer_cls = hit
    return layer_cls(schema)


def classify_instance(inst):
    """Classify an ifcopenshell entity instance by walking up its supertypes.

    Unlike classify(), this function walks the inheritance chain because a
    concrete instance genuinely inherits its supertype's layer.
    """
    direct = classify(inst.is_a())
    if direct is not None:
        return direct
        
    for parent in _supertypes(inst):
        found = classify(parent)
        if found is not None:
            return found
            
    return None


def _supertypes(inst):
    """Yield supertype names of an instance, nearest first."""
    try:
        decl = inst.wrapped_data.declaration()
    except Exception:
        return
        
    decl = getattr(decl, "as_entity", lambda: None)()
    if decl is None:
        return
        
    current = decl.supertype()
    while current is not None:
        yield current.name()
        current = current.supertype()