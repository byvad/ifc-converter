import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Dialogs


ApplicationWindow {
    id: window
    visible: true
    width: 1140
    height: 780
    title: "IFC to OBJ"
    color: Style.paper

    // Exposed to Python
    signal fileDropped(string path)
    signal convertRequested(string source, string target, string upAxis, string layer, bool toMetres, bool useColors, bool keepGlass)

    DropArea {
        anchors.fill: parent
        onDropped: (drop) => {
            if (drop.hasUrls && drop.urls.length > 0) {
                let path = drop.urls[0].toString()
                if (path.toLowerCase().endsWith(".ifc")) {
                    // Strip file:/// prefix for python
                    window.fileDropped(drop.urls[0].toString())
                }
            }
        }

        RowLayout {
            anchors.fill: parent
            spacing: 0

            DescentRail {
                id: sidebar
                objectName: "sidebar"
                Layout.fillHeight: true
            }

            ColumnLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                Layout.margins: 20
                spacing: 14

                // Header
                ColumnLayout {
                    spacing: 1
                    Text {
                        text: "IFC4 / IFC4X3  →  WAVEFRONT OBJ"
                        color: Style.inkFaint
                        font.pixelSize: 10
                        font.letterSpacing: 1.4
                    }
                    Text {
                        text: "Layer descent converter"
                        color: Style.ink
                        font.pixelSize: 19
                        font.weight: Font.DemiBold
                        font.letterSpacing: -0.3
                    }
                }

                // Source Card
                Card {
                    Layout.fillWidth: true
                    implicitHeight: 120

                    GridLayout {
                        anchors.fill: parent
                        columns: 3
                        columnSpacing: 9
                        rowSpacing: 8

                        Text { text: "Source"; color: Style.ink }
                        TextField { 
                            id: sourceField
                            objectName: "sourceField"
                            Layout.fillWidth: true
                            placeholderText: "Choose an .ifc file, or drop one here"
                        }
                        Button { 
                            text: "Browse"
                            onClicked: fileDialog.open()
                        }

                        Text { text: "Write to"; color: Style.ink }
                        TextField {
                            id: targetField
                            objectName: "targetField"
                            Layout.fillWidth: true
                            placeholderText: "Set automatically from the source file"
                        }
                        Button { text: "Change" }
                    }
                }

                // Splitter Area
                SplitView {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    orientation: Qt.Horizontal

                    Card {
                        title: "What is in the file"
                        SplitView.preferredWidth: 460
                        SplitView.fillHeight: true
                        
                        ListView {
                            id: contentsTree
                            objectName: "contentsTree"
                            anchors.fill: parent
                            clip: true
                            model: []
                            spacing: 2
                            ScrollBar.vertical: ScrollBar {}

                            delegate: Item {
                                width: ListView.view.width
                                height: 22

                                Text {
                                    anchors.left: parent.left
                                    anchors.leftMargin: modelData.depth * 16
                                    anchors.verticalCenter: parent.verticalCenter
                                    width: parent.width - 74 - modelData.depth * 16
                                    elide: Text.ElideRight
                                    text: modelData.label
                                    color: modelData.depth === 0
                                           ? Style.layerColor(modelData.layer)
                                           : modelData.depth === 1 ? Style.inkMuted
                                                                   : Style.ink
                                    font.pixelSize: modelData.depth === 0 ? 12 : 11
                                    font.weight: modelData.depth === 0 ? Font.DemiBold
                                                                       : Font.Normal
                                    font.family: modelData.depth === 2
                                                 ? "JetBrains Mono"
                                                 : Qt.application.font.family
                                }

                                Text {
                                    anchors.right: parent.right
                                    anchors.rightMargin: 14
                                    anchors.verticalCenter: parent.verticalCenter
                                    text: modelData.count
                                    color: Style.inkMuted
                                    font.pixelSize: 11
                                    font.family: "JetBrains Mono"
                                }
                            }
                        }
                    }

                    Card {
                        title: "Conversion"
                        SplitView.preferredWidth: 400
                        SplitView.fillHeight: true

                        ColumnLayout {
                            anchors.fill: parent
                            
                            GridLayout {
                                columns: 2
                                Text { text: "Up axis" }
                                ComboBox { 
                                    id: upAxisCombo
                                    Layout.fillWidth: true
                                    model: ["Z up  ·  IFC native", "Y up  ·  most OBJ viewers"] 
                                }
                                
                                Text { text: "Convert" }
                                ComboBox { 
                                    id: layerCombo
                                    Layout.fillWidth: true
                                    model: ["All layers", "Domain", "Interoperability", "Core", "Resource"]
                                }
                            }

                            CheckBox { id: chkMetres; text: "Rescale to metres using the file's length unit"; checked: true }
                            CheckBox { id: chkColors; text: "Resolve colours from IFC styles and materials"; checked: true }
                            CheckBox { id: chkGlass; text: "Keep fully transparent glazing visible"; checked: true; enabled: chkColors.checked }

                            TextArea {
                                id: reportArea
                                objectName: "reportArea"
                                Layout.fillWidth: true
                                Layout.fillHeight: true
                                readOnly: true
                                placeholderText: "The conversion report appears here."
                                font.family: "JetBrains Mono"
                                background: Rectangle { color: Style.panel; border.color: Style.rule; radius: 6 }
                            }
                        }
                    }
                }

                // Action Bar
                RowLayout {
                    Layout.fillWidth: true
                    spacing: 12

                    Text {
                        id: statusLabel
                        objectName: "statusLabel"
                        text: ""
                        color: Style.inkFaint
                        Layout.fillWidth: true
                    }

                    ProgressBar {
                        id: progressBar
                        objectName: "progressBar"
                        visible: false
                        Layout.preferredWidth: 200
                    }

                    Button {
                        text: "Convert"
                        // Add customized Primary Button styling here
                        onClicked: {
                            window.convertRequested(
                                sourceField.text, targetField.text,
                                upAxisCombo.currentIndex === 0 ? "z" : "y",
                                layerCombo.currentIndex === 0 ? "" : layerCombo.currentText,
                                chkMetres.checked, chkColors.checked, chkGlass.checked
                            )
                        }
                    }
                }
            }
        }
    }

    FileDialog {
        id: fileDialog
        nameFilters: ["IFC files (*.ifc)", "All files (*)"]
        onAccepted: window.fileDropped(selectedFile.toString())
    }
}