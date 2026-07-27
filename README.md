# IFC Converter

## Introduction

This repository contains a utility for converting Industry Foundation Classes (IFC) files into Wavefront OBJ models. The tool generates both the OBJ geometry file and the associated MTL material definition file.

## Requirements

- Python 3.x
- Dependencies listed in `requirements.txt`

## Installation

Install the required Python packages:

    pip install -r requirements.txt

## Usage

Execute the converter using the Python interpreter:

    py main.py

## Output

Upon successful conversion, the tool creates an output directory within the `output` folder. The directory is named after the source IFC file and includes:

- OBJ file
- MTL file

For example, converting `VeryBeautifulHouse.ifc` produces:

    output/VeryBeautifulHouse/VeryBeautifulHouse.obj
    output/VeryBeautifulHouse/VeryBeautifulHouse.mtl