# @author: Davy Bellens

"""Product schema relationships: handling voiding elements."""


def openings_of(product):
    """Yield IfcOpeningElement instances voiding this product.

    IfcRelVoidsElement is a Product-schema (Core) relationship. The opening's
    geometry is the *void volume*, not material, which is exactly why exporting
    openings as solids fills every doorway with a block.
    """
    for rel in getattr(product, "HasOpenings", None) or []:
        if rel.is_a("IfcRelVoidsElement"):
            yield rel.RelatedOpeningElement