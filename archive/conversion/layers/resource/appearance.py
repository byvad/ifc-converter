# @author: Davy Bellens

"""Resource layer: Presentation Appearance and Material schemas."""

def _rgb_of(colour):
    if colour is None or not colour.is_a("IfcColourRgb"):
        return None
    try:
        return (float(colour.Red), float(colour.Green), float(colour.Blue))
    except (TypeError, ValueError, AttributeError):
        return None


def surface_style_rgba(style):
    if style is None or not style.is_a("IfcSurfaceStyle"):
        return None

    for shading in style.Styles or []:
        if not shading.is_a("IfcSurfaceStyleShading"):
            continue

        rgb = _rgb_of(getattr(shading, "SurfaceColour", None))
        if rgb is None:
            continue

        alpha = 1.0
        transparency = getattr(shading, "Transparency", None)
        if transparency is not None:
            try:
                alpha = 1.0 - float(transparency)
            except (TypeError, ValueError):
                alpha = 1.0
        return (rgb[0], rgb[1], rgb[2], max(0.0, min(1.0, alpha)))

    return None


def styled_item_rgba(styled_item):
    if styled_item is None:
        return None

    for entry in styled_item.Styles or []:
        if entry.is_a("IfcPresentationStyleAssignment"):
            for inner in entry.Styles or []:
                rgba = surface_style_rgba(inner)
                if rgba is not None:
                    return rgba
        else:
            rgba = surface_style_rgba(entry)
            if rgba is not None:
                return rgba

    return None


def item_rgba(item):
    try:
        styled = getattr(item, "StyledByItem", None)
    except (AttributeError, RuntimeError):
        return None

    for styled_item in styled or []:
        rgba = styled_item_rgba(styled_item)
        if rgba is not None:
            return rgba
    return None


_MATERIAL_SET_LISTS = {
    "IfcMaterialLayerSet": "MaterialLayers",
    "IfcMaterialList": "Materials",
    "IfcMaterialProfileSet": "MaterialProfiles",
    "IfcMaterialConstituentSet": "MaterialConstituents",
}

_MATERIAL_USAGES = {
    "IfcMaterialLayerSetUsage": "ForLayerSet",
    "IfcMaterialProfileSetUsage": "ForProfileSet",
}

_MATERIAL_HOLDERS = ("IfcMaterialLayer", "IfcMaterialProfile",
                     "IfcMaterialConstituent")


def materials_of(definition, depth=0):
    if definition is None or depth > 6:
        return []

    name = definition.is_a()

    if name == "IfcMaterial":
        return [definition]

    if name in _MATERIAL_USAGES:
        return materials_of(getattr(definition, _MATERIAL_USAGES[name], None),
                            depth + 1)

    if name in _MATERIAL_SET_LISTS:
        out = []
        for child in getattr(definition, _MATERIAL_SET_LISTS[name], None) or []:
            out.extend(materials_of(child, depth + 1))
        return out

    if name in _MATERIAL_HOLDERS:
        return materials_of(getattr(definition, "Material", None), depth + 1)

    return []


UNSTYLED_NAME = "ifc_unstyled"
UNSTYLED_RGBA = (0.78, 0.78, 0.78, 1.0)


class Palette:
    def __init__(self, model, min_alpha=0.0, linear=False):
        self.min_alpha = min_alpha
        self.linear = linear
        self.materials = {}
        self._material_rgba = {}
        self._product_rgba = {}
        self._index(model)

    def _index(self, model):
        try:
            definitions = model.by_type("IfcMaterialDefinitionRepresentation")
        except RuntimeError:
            return

        for definition in definitions:
            material = getattr(definition, "RepresentedMaterial", None)
            if material is None:
                continue
            for representation in definition.Representations or []:
                for item in getattr(representation, "Items", None) or []:
                    if not item.is_a("IfcStyledItem"):
                        continue
                    rgba = styled_item_rgba(item)
                    if rgba is not None:
                        self._material_rgba.setdefault(material.id(), rgba)
                        break

    def product_rgba(self, product):
        key = product.id()
        if key in self._product_rgba:
            return self._product_rgba[key]

        rgba = None
        for association in getattr(product, "HasAssociations", None) or []:
            if not association.is_a("IfcRelAssociatesMaterial"):
                continue
            for material in materials_of(
                    getattr(association, "RelatingMaterial", None)):
                rgba = self._material_rgba.get(material.id())
                if rgba is not None:
                    break
            if rgba is not None:
                break

        self._product_rgba[key] = rgba
        return rgba

    def unstyled(self):
        self.materials.setdefault(UNSTYLED_NAME, UNSTYLED_RGBA)
        return UNSTYLED_NAME

    def register(self, rgba):
        if rgba is None:
            return None
        r, g, b, a = rgba
        if self.min_alpha:
            a = max(a, self.min_alpha)
        clamped = tuple(max(0.0, min(1.0, c)) for c in (r, g, b, a))
        name = "ifc_%02X%02X%02X%02X" % tuple(
            int(round(c * 255)) for c in clamped)
        self.materials.setdefault(name, clamped)
        return name

    def write_mtl(self, path, source=""):
        with open(path, "w") as mtl:
            mtl.write("# materials resolved from %s\n" % source)
            mtl.write("# IfcSurfaceStyleRendering -> Kd, Transparency -> d\n")
            for name in sorted(self.materials):
                r, g, b, a = self.materials[name]
                if self.linear:
                    r, g, b = (srgb_to_linear(c) for c in (r, g, b))
                mtl.write("\nnewmtl %s\n" % name)
                mtl.write("Kd %.6f %.6f %.6f\n" % (r, g, b))
                mtl.write("Ka 0.000000 0.000000 0.000000\n")
                mtl.write("Ks 0.000000 0.000000 0.000000\n")
                mtl.write("Ns 10.0\n")
                mtl.write("d %.4f\n" % a)
                mtl.write("illum %d\n" % (4 if a < 0.999 else 2))
        return len(self.materials)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4