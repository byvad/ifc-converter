"""IFC -> OBJ, walking the conceptual layers by hand.

ifcopenshell is used here only as a STEP parser: it reads the file into an
entity graph and resolves the #123 references. Every geometric decision after
that is made in this package, one layer at a time.

The descent, in full:

    product_layer   Domain / Interoperability
                    "which products am I converting?"
                        |
    core_layer      Core (Kernel, Product)
                    "what is this product's placement and representation?"
                        |
    resource_layer  Resource (Geometry, Profile, Geometric Model, Topology)
                    "what are the actual coordinates?"
                        |
                    Mesh -> OBJ

Usage:
    python convert.py model.ifc out.obj
    python convert.py model.ifc --layer Interoperability
    python convert.py model.ifc --schema HVAC Electrical
    python convert.py model.ifc --report
"""

import argparse
import os
import re
import sys

import ifcopenshell

import core_layer
import product_layer


class ConversionReport:
    def __init__(self):
        self.converted = 0
        self.empty = 0
        self.vertices = 0
        self.triangles = 0
        self.unsupported = {}
        self.skipped_reps = {}
        self.layer_counts = {}
        self.schema_counts = {}
        self.unit_scale = 1.0
        self.up_axis = "z"
        self.items_built = 0

    def note_unsupported(self, names):
        for name in names:
            key = name.split(":")[0]
            self.unsupported[key] = self.unsupported.get(key, 0) + 1

    def note_skipped(self, identifiers):
        for identifier in identifiers:
            key = identifier or "(unnamed)"
            self.skipped_reps[key] = self.skipped_reps.get(key, 0) + 1

    def render(self):
        lines = []
        lines.append(f"Length unit scale : {self.unit_scale} m per model unit")
        lines.append(f"Up axis           : {self.up_axis.upper()}")
        lines.append(f"Products meshed   : {self.converted}")
        lines.append(f"Products empty    : {self.empty}")
        lines.append(f"Resource items    : {self.items_built}")
        lines.append(f"Vertices          : {self.vertices}")
        lines.append(f"Triangles         : {self.triangles}")

        if self.layer_counts:
            lines.append("")
            lines.append("Products by conceptual layer:")
            for name in product_layer.LAYER_ORDER + ["Unclassified"]:
                if name in self.layer_counts:
                    lines.append(f"  {name:<18} {self.layer_counts[name]}")

        if self.schema_counts:
            lines.append("")
            lines.append("Products by schema:")
            for key in sorted(self.schema_counts):
                lines.append(f"  {key:<40} {self.schema_counts[key]}")

        if self.skipped_reps:
            lines.append("")
            lines.append("Representations skipped (not body geometry):")
            for key, count in sorted(self.skipped_reps.items()):
                lines.append(f"  {key:<28} {count}")

        if self.unsupported:
            lines.append("")
            lines.append("Unsupported geometry items:")
            for key, count in sorted(self.unsupported.items(), key=lambda kv: -kv[1]):
                lines.append(f"  {key:<40} {count}")

        return "\n".join(lines)


def sanitise(name):
    if not name:
        return "unnamed"
    return re.sub(r"[^\w.-]+", "_", str(name).strip()) or "unnamed"


def convert(ifc_path, obj_path, layers=None, schemas=None, classes=None,
            to_metres=True, up_axis="z", progress=None, cancelled=None):
    if not os.path.isfile(ifc_path):
        raise FileNotFoundError(ifc_path)

    model = ifcopenshell.open(ifc_path)
    report = ConversionReport()

    # Resource layer, Measure schema: how big is one length unit?
    scale = core_layer.unit_scale(model) if to_metres else 1.0
    report.unit_scale = scale
    report.up_axis = up_axis

    # --- Top of the descent: Domain / Interoperability ------------------
    selection = product_layer.select(
        model, layers=layers, schemas=schemas, classes=classes
    )
    report.layer_counts = selection.by_layer()
    report.schema_counts = selection.by_schema()

    vertex_offset = 0

    with open(obj_path, "w") as obj:
        obj.write(f"# {os.path.basename(ifc_path)} -> OBJ\n")
        obj.write(f"# schema {model.schema}, unit scale {scale} m\n")
        obj.write("# converted by layer descent: Domain -> Core -> Resource\n")
        obj.write(f"# up axis {up_axis.upper()} "
                  f"({'IFC native' if up_axis == 'z' else 'rotated from IFC Z-up'})\n\n")

        total = len(selection)
        for index, (product, layer) in enumerate(selection):
            if cancelled is not None and cancelled():
                break
            if progress is not None:
                progress(index, total, product)

            # --- Core layer: placement + representation ------------------
            geometry = core_layer.resolve(product)

            report.items_built += geometry.items_built
            report.note_unsupported(geometry.unsupported)
            report.note_skipped(geometry.skipped_representations)

            if not geometry.has_geometry:
                report.empty += 1
                continue

            mesh = geometry.mesh.scaled(scale) if scale != 1.0 else geometry.mesh
            if up_axis == "y":
                mesh = mesh.to_y_up()

            label = sanitise(geometry.name)
            layer_tag = layer.layer_type.replace(" ", "_") if layer else "Unclassified"
            obj.write(f"o {label}_{geometry.guid}\n")
            obj.write(f"# {product.is_a()} | layer {layer_tag}\n")

            for x, y, z in mesh.vertices:
                obj.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")

            for a, b, c in mesh.triangles:
                obj.write(
                    f"f {a + 1 + vertex_offset} "
                    f"{b + 1 + vertex_offset} "
                    f"{c + 1 + vertex_offset}\n"
                )
            obj.write("\n")

            vertex_offset += len(mesh.vertices)
            report.converted += 1
            report.triangles += len(mesh.triangles)

    report.vertices = vertex_offset
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Convert IFC to OBJ by descending the conceptual layers."
    )
    parser.add_argument("ifc_file")
    parser.add_argument("obj_file", nargs="?")
    parser.add_argument("--layer", nargs="+", dest="layers",
                        choices=product_layer.LAYER_ORDER,
                        help="Only convert products in these conceptual layers")
    parser.add_argument("--schema", nargs="+", dest="schemas",
                        help='Only convert these schemas, e.g. HVAC "Shared Building"')
    parser.add_argument("--class", nargs="+", dest="classes",
                        help="Only convert these IFC classes, e.g. IfcWall")
    parser.add_argument("--up-axis", choices=["z", "y"], default="z",
                        help="Output up-axis. IFC is Z-up; most OBJ viewers "
                             "assume Y-up, which makes models look tipped over.")
    parser.add_argument("--keep-units", action="store_true",
                        help="Do not rescale to metres")
    parser.add_argument("--report", action="store_true",
                        help="Print the layer breakdown")
    args = parser.parse_args(argv)

    obj_file = args.obj_file or os.path.splitext(args.ifc_file)[0] + ".obj"

    report = convert(
        args.ifc_file,
        obj_file,
        layers=set(args.layers) if args.layers else None,
        schemas=set(args.schemas) if args.schemas else None,
        classes=set(args.classes) if args.classes else None,
        to_metres=not args.keep_units,
        up_axis=args.up_axis,
    )

    print(f"Wrote {obj_file}")
    if args.report:
        print()
        print(report.render())
    else:
        print(f"{report.converted} products, {report.vertices} vertices, "
              f"{report.triangles} triangles")
    return 0


if __name__ == "__main__":
    sys.exit(main())
