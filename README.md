# IFC Converter

## Introduction

This repository contains a utility for converting Industry Foundation Classes (IFC) files into Wavefront OBJ models. The tool generates both the OBJ geometry file and the associated MTL material definition file.

It is a modular, layer-by-layer converter. Rather than treating the IFC file as a flat list of geometry, this converter acts as a conceptual layer descent. It utilizes `ifcopenshell` purely as a STEP parser to read the entity graph, making all subsequent geometric and material decisions internally by walking down the IFC hierarchy.

### Architecture: The Layer Descent

The conversion process is structured around the four IFC conceptual layers, moving from abstract definitions down to concrete geometry:

* **Domain & Interoperability (Selection):** The entry point of the descent, determining exactly which products should be converted.
* **Core (Placement & Representation):** Resolves the spatial placement and representation for the selected products.
* **Resource (Geometry & Topology):** Handles the mathematical generation of the meshes and actual coordinates.

## Requirements

- Python 3.x
- Dependencies listed in `requirements.txt`

## Installation

Install the required Python packages:

    pip install -r requirements.txt

## Command-Line Interface (CLI)

Execute the converter using the Python interpreter:

    python convert.py <ifc_file> [<obj_file>] [options]

**Configuration Arguments:**

| Argument | Description |
| :--- | :--- |
| `--layer` | Restricts conversion to specific conceptual layers. |
| `--schema` | Filters the conversion by specific IFC schemas. |
| `--class` | Limits conversion to exact IFC classes. |
| `--up-axis` | Sets the output up-axis to `z` (IFC native) or `y` (most OBJ viewers). |
| `--keep-units` | Do not rescale the geometry to metres. |
| `--no-colour` | Skips appearance resolution and writes no `.mtl`. |
| `--min-alpha` | Sets a minimum opacity floor on transparency. |
| `--linear` | Converts color profiles from sRGB to linear before writing. |
| `--report` | Prints the layer breakdown and conversion report. |
| `--out-root` | Defines the root directory for the per-model output folders. |

## Desktop GUI Application

For a more visual workflow, the project includes a standalone desktop application built with PySide6 and QML.

**Key Features:**

* **Drag-and-Drop:** Quickly load `.ifc` files by dropping them into the application.
* **Pre-conversion Inspection:** The app reads the file and displays a hierarchical tree of the file's contents.
* **Granular Statistics:** The inspection view categorizes and counts the internal products by layer, schema, and specific IFC class.
* **Visual Toggles:** Exposes core CLI features—like Y-up axis rotation, unit rescaling, color resolution, and glass transparency preservation—through UI checkboxes and dropdowns.

## Unity Integration

A custom C# Editor plugin is provided to convert and import IFC models seamlessly inside the Unity Editor without relying on external third-party OBJ importers.

1. Place the `IFCImporterWindow.cs` script into an `Editor` folder inside your Unity project (e.g., `Assets/Editor/`).
2. In the Unity top menu bar, navigate to **IFC Converter > Import IFC model**.

![Unity Menu Bar](img/menu-bar.png)

3. This opens the importer panel.

![Unconfigured Importer Panel](img/import-menu.png)

4. In the **Environment Configuration** section, point the tool to your local Python executable and the `convert.py` script.
5. Select your source `.ifc` file, adjust your desired settings (such as keeping the Unity-friendly Y-Up rotation), and set your output folder (defaults to `Assets/ImportedModels`).

![Configured Importer Panel](img/configured-import-menu.png)

6. Click **Convert & Import IFC**. The tool will run the Python conversion descent, load the generated files into the Asset Database, and automatically instantiate the 3D model into your active scene.

## Output

Upon successful conversion, the tool creates an output directory within the `output` folder. The directory is named after the source IFC file and includes:

- OBJ file
- MTL file

For example, converting `VeryBeautifulHouse.ifc` produces:

    output/VeryBeautifulHouse/VeryBeautifulHouse.obj
    output/VeryBeautifulHouse/VeryBeautifulHouse.mtl