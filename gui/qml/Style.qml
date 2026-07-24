pragma Singleton
import QtQuick

QtObject {
    readonly property color paper: "#FBFBFC"
    readonly property color panel: "#FFFFFF"
    readonly property color rule: "#E3E5E9"
    readonly property color ruleStrong: "#CDD1D8"
    readonly property color ink: "#1E2126"
    readonly property color inkMuted: "#6B7280"
    readonly property color inkFaint: "#9AA1AC"
    readonly property color accent: "#1D4ED8"

    function layerColor(name) {
        const colors = {
            "Domain": "#D97706",
            "Interoperability": "#0F766E",
            "Core": "#1D4ED8",
            "Resource": "#5B21B6",
            "Unclassified": "#9AA1AC"
        }
        return colors[name] || inkFaint
    }

    function layerBlurb(name, hasData) {
        const blurbs = {
            "Domain": "Discipline-specific products",
            "Interoperability": "Shared building elements",
            "Core": "Placement and representation",
            "Resource": "Solids meshed"
        }
        const units = {
            "Domain": "products",
            "Interoperability": "products",
            "Core": "products",
            "Resource": "items"
        }
        let blurb = blurbs[name] || ""
        if (hasData) {
            blurb += `  ·  ${units[name] || ""}`
        }
        return blurb
    }
}