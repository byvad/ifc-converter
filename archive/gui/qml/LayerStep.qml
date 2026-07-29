import QtQuick
import QtQuick.Layouts

Item {
    id: root
    implicitHeight: 66
    Layout.fillWidth: true

    property string name: ""
    property bool isLast: false
    property var count: undefined // undefined means no data, 0 means empty
    property bool active: false

    readonly property bool hasData: count !== undefined
    readonly property bool isZero: hasData && count === 0
    readonly property color stepColor: Style.layerColor(name)

    // Vertical connecting line
    Rectangle {
        x: 18 - width/2
        y: 22 + 9 
        width: 2
        height: root.height - y
        color: Style.ruleStrong
        visible: !root.isLast
    }

    // The Dot/Ring
    Rectangle {
        x: 18 - width/2
        y: 22 - height/2
        width: 10
        height: 10
        radius: 5
        color: (hasData && !isZero) ? stepColor : Style.panel
        border.color: isZero ? Qt.rgba(stepColor.r, stepColor.g, stepColor.b, 0.35) : 
                     (hasData ? "transparent" : Style.ruleStrong)
        border.width: (hasData && !isZero) ? 0 : 2

        // Active Halo
        Rectangle {
            anchors.centerIn: parent
            width: 20
            height: 20
            radius: 10
            color: Qt.rgba(stepColor.r, stepColor.g, stepColor.b, 0.15)
            visible: root.active && hasData && !isZero
            z: -1
        }
    }

    // Texts
    Column {
        x: 36
        y: 17
        spacing: 2

        Text {
            text: root.name
            color: (hasData && !isZero) ? Style.ink : Style.inkFaint
            font.pixelSize: 13
            font.weight: Font.DemiBold
        }

        Text {
            text: Style.layerBlurb(root.name, root.hasData)
            color: Style.inkFaint
            font.pixelSize: 10
        }
    }

    // Count (Right aligned)
    Text {
        anchors.right: parent.right
        anchors.rightMargin: 16
        y: 12
        text: hasData ? count.toString() : "—"
        color: (hasData && !isZero) ? stepColor : Style.inkFaint
        font.family: "JetBrains Mono" // Ensure a monospaced font is loaded
        font.pixelSize: 13
    }
}