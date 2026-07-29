# Third Party Notices

This package's source code (the C# runtime, editor, and geometry pipeline) is
original work by the author, licensed under the MIT License — see LICENSE.md.

Two things bundled or used alongside that code come from elsewhere and carry
their own terms:

## IFC schema data (Runtime/Resources/IfcSchemas/ifc2x3.txt, ifc4.txt)

These files are a machine-generated extraction (entity names, attribute
order, and type inheritance) of the IFC EXPRESS schema.

The IFC specification is:

> Copyright buildingSMART International Limited.
> Licensed under the Creative Commons Attribution-NoDerivatives 4.0
> International License (CC BY-ND 4.0).
> https://creativecommons.org/licenses/by-nd/4.0/

buildingSMART®, Home of openBIM®, openBIM®, and IFC™ are trademarks of
buildingSMART International Limited. See buildingSMART's Brand Usage
Guidelines for correct use.

Source specification: https://technical.buildingsmart.org/standards/ifc/

## Build-time tooling (tools/generate.py)

The `.schema` tables above are produced from the official EXPRESS schema
using ifcopenshell (https://ifcopenshell.org), licensed under the GNU
Lesser General Public License v3.0 (LGPL-3.0-or-later).

ifcopenshell is a build-time dependency only. It is not distributed with
this package, not compiled into it, and not required at runtime or by
anyone installing the package — only by anyone regenerating the schema
tables from a newer IFC release.
