Two ways to import it

1. add it to the `manifest.json` in your Unity-project in the Packages/ directory.

e.g.

    {
        "dependencies": {
            "com.byvad.ifc-import": "https://github.com/byvad/ifc-converter.git?path=/UnityPackage/com.byvad.ifc-import",
            "com.unity.render-pipelines.universal": "17.0.0"
        }
    }

2. Add it by URL via UPM: `https://github.com/byvad/ifc-converter.git?path=/UnityPackage/com.byvad.ifc-import`

TODO:

- Create package with version number