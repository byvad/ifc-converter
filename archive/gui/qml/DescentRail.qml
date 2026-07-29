import QtQuick
import QtQuick.Layouts

Rectangle {
    id: root
    width: 252
    color: Style.panel
    border.color: Style.rule
    border.width: 1
    
    // Only border on the right
    Rectangle { anchors.fill: parent; color: "transparent"; border.width: 0 }

    property var layerCounts: ({})
    property string activeLayer: ""

    ColumnLayout {
        anchors.fill: parent
        anchors.topMargin: 20
        anchors.leftMargin: 18
        anchors.rightMargin: 12
        anchors.bottomMargin: 18
        spacing: 0

        Text {
            text: "REFERENCE DIRECTION"
            color: Style.inkFaint
            font.pixelSize: 10
            font.letterSpacing: 1.4
            Layout.bottomMargin: 10
        }

        Repeater {
            model: ["Domain", "Interoperability", "Core", "Resource"]
            LayerStep {
                name: modelData
                isLast: index === 3
                count: root.layerCounts[modelData]
                active: root.activeLayer === modelData
            }
        }

        Item { Layout.fillHeight: true } // Spacer

        Text {
            text: "References only ever point downward, so extracting geometry is always a descent."
            color: Style.inkFaint
            font.pixelSize: 11
            wrapMode: Text.WordWrap
            Layout.fillWidth: true
            Layout.topMargin: 14
        }
    }
}