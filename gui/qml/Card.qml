import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: root
    property string title: ""
    default property alias content: container.data

    color: Style.panel
    border.color: Style.rule
    border.width: 1
    radius: 6
    clip: true

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 14
        spacing: 9

        Text {
            text: root.title.toUpperCase()
            visible: root.title.length > 0
            color: Style.inkMuted
            font.pixelSize: 11
            font.letterSpacing: 0.8
            Layout.fillWidth: true
        }

        Item {
            id: container
            Layout.fillWidth: true
            Layout.fillHeight: true
        }
    }
}