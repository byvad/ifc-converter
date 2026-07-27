"""Emit a compact entity/attribute/inverse table for the C# seam.

Run once per schema and check the output in; it is generated data, not
something anyone should be hand-editing. Requires ifcopenshell, which is a
build-time dependency only -- the runtime never sees it.
"""
import sys
from ifcopenshell import ifcopenshell_wrapper as W

def emit(schema_name, out_path):
    schema = W.schema_by_name(schema_name)
    lines = ["S|" + schema_name]
    entities = []
    for decl in schema.declarations():
        ent = decl.as_entity()
        if ent is not None:
            entities.append(ent)
    entities.sort(key=lambda e: e.name())

    for ent in entities:
        super_decl = ent.supertype()
        super_name = super_decl.name() if super_decl is not None else "-"

        inherited = super_decl.all_attributes() if super_decl is not None else []
        own = ent.all_attributes()[len(inherited):]
        lines.append("E|%s|%s|%s" % (ent.name(), super_name,
                                     ",".join(a.name() for a in own)))

        inherited_inv = {i.name() for i in (super_decl.all_inverse_attributes()
                                            if super_decl is not None else [])}
        for inv in ent.all_inverse_attributes():
            if inv.name() in inherited_inv:
                continue
            lines.append("V|%s|%s|%s|%s" % (
                ent.name(), inv.name(),
                inv.entity_reference().name(),
                inv.attribute_reference().name()))

    text = "\n".join(lines) + "\n"
    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return len(entities), len(text)

for name in ("IFC2X3", "IFC4"):
    count, size = emit(name, name.lower() + ".schema")
    print(f"{name}: {count} entities, {size/1024:.1f} KB")
