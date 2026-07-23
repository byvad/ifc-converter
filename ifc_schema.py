"""The IFC conceptual layer taxonomy.

The four layers are a *dependency* rule, not a data flow. An entity in a given
layer may reference entities in its own layer or any layer below it, and never
above. Domain sits on top; Resource sits at the bottom.

    Domain            IfcAirTerminal, IfcLightFixture, IfcPile
    Interoperability  IfcWall, IfcSlab, IfcDoor, IfcFurniture
    Core              IfcProduct, IfcRelAggregates, IfcBuildingStorey
    Resource          IfcCartesianPoint, IfcExtrudedAreaSolid, IfcPolyline

Because references only point downward, resolving a product's geometry is
always a descent: start at a Domain or Interoperability product, pass through
Core to reach its placement and representation, and bottom out in Resource
where the actual coordinates live.
"""

DL_SCHEMAS = {
    "Building Controls", "Plumbing FireProtections", "Structural Elements",
    "Structural Analysis", "HVAC", "Electrical", "Architecture",
    "Construction Management",
}
IO_SCHEMAS = {
    "Shared Bldg Services", "Shared Components", "Shared Building",
    "Shared Management", "Shared Facilities",
}
CORE_SCHEMAS = {"Control", "Product", "Process", "Kernel"}
RSRC_SCHEMAS = {
    "DateTime", "Material", "External Reference", "Geometric Constraint",
    "Geometric Model", "Geometry", "Actor", "Profile", "Property", "Quantity",
    "Topology", "Utility", "Measure", "Presentation Appearance",
    "Presentation Definition", "Presentation Organization", "Representation",
    "Constraint", "Approval", "Structural Load", "Cost",
}


class Layer:
    """A conceptual layer, holding the set of schemas legal within it."""

    def __init__(self, layer_name, types, layer_type):
        self._layer_name = layer_name
        self._layer_types = types
        self.layer_type = layer_type

    @property
    def layer_name(self):
        return self._layer_name

    @property
    def layer_type(self):
        return self._layer_type

    @layer_type.setter
    def layer_type(self, value):
        if value not in self._layer_types:
            raise ValueError(f"Invalid {self._layer_name} Layer Schema: {value!r}")
        self._layer_type = value

    def __repr__(self):
        return f"{type(self).__name__}({self._layer_type!r})"


class Domain(Layer):
    def __init__(self, domain_type):
        super().__init__("Domain", DL_SCHEMAS, domain_type)


class InterOperability(Layer):
    def __init__(self, io_type):
        super().__init__("Interoperability", IO_SCHEMAS, io_type)


class Core(Layer):
    def __init__(self, core_type):
        super().__init__("Core", CORE_SCHEMAS, core_type)


class Resource(Layer):
    def __init__(self, resource_type):
        super().__init__("Resource", RSRC_SCHEMAS, resource_type)


# --------------------------------------------------------------------------
# Which schema does a given IFC entity belong to?
#
# This is deliberately partial. It covers the entities a geometry converter
# actually meets, which is a small slice of the ~800 in IFC4. Anything not
# listed resolves to None and is reported as unclassified rather than guessed.
# --------------------------------------------------------------------------

_ENTITY_SCHEMA = {}


def _register(schema, layer_cls, entities):
    for name in entities:
        _ENTITY_SCHEMA[name.upper()] = (schema, layer_cls)


# --- Domain layer ---------------------------------------------------------
_register("HVAC", Domain, [
    "IfcAirTerminal", "IfcAirTerminalBox", "IfcBoiler", "IfcChiller",
    "IfcCoil", "IfcCompressor", "IfcCondenser", "IfcDuctFitting",
    "IfcDuctSegment", "IfcDuctSilencer", "IfcEvaporator", "IfcFan",
    "IfcFilter", "IfcHeatExchanger", "IfcHumidifier", "IfcPump",
    "IfcSpaceHeater", "IfcTank", "IfcTubeBundle", "IfcUnitaryEquipment",
    "IfcValve", "IfcVibrationIsolator",
])
_register("Electrical", Domain, [
    "IfcAudioVisualAppliance", "IfcCableCarrierFitting",
    "IfcCableCarrierSegment", "IfcCableFitting", "IfcCableSegment",
    "IfcCommunicationsAppliance", "IfcElectricAppliance",
    "IfcElectricDistributionBoard", "IfcElectricFlowStorageDevice",
    "IfcElectricGenerator", "IfcElectricMotor", "IfcElectricTimeControl",
    "IfcJunctionBox", "IfcLamp", "IfcLightFixture", "IfcMotorConnection",
    "IfcOutlet", "IfcProtectiveDevice", "IfcSolarDevice",
    "IfcSwitchingDevice", "IfcTransformer",
])
_register("Building Controls", Domain, [
    "IfcActuator", "IfcAlarm", "IfcController", "IfcFlowInstrument",
    "IfcProtectiveDeviceTrippingUnit", "IfcSensor", "IfcUnitaryControlElement",
])
_register("Plumbing FireProtections", Domain, [
    "IfcFireSuppressionTerminal", "IfcInterceptor", "IfcSanitaryTerminal",
    "IfcStackTerminal", "IfcWasteTerminal",
])
_register("Structural Elements", Domain, [
    "IfcFooting", "IfcPile", "IfcReinforcingBar", "IfcReinforcingMesh",
    "IfcTendon", "IfcTendonAnchor",
])
_register("Structural Analysis", Domain, [
    "IfcStructuralCurveMember", "IfcStructuralPointConnection",
    "IfcStructuralSurfaceMember", "IfcStructuralAnalysisModel",
])
_register("Architecture", Domain, [
    "IfcDoorStyle", "IfcWindowStyle", "IfcDoorLiningProperties",
    "IfcWindowLiningProperties", "IfcPermeableCoveringProperties",
])
_register("Construction Management", Domain, [
    "IfcConstructionEquipmentResource", "IfcConstructionMaterialResource",
    "IfcConstructionProductResource", "IfcCrewResource", "IfcLaborResource",
    "IfcSubContractResource",
])

# --- Interoperability layer ----------------------------------------------
_register("Shared Building", InterOperability, [
    "IfcBeam", "IfcBuildingElementPart", "IfcBuildingElementProxy",
    "IfcChimney", "IfcColumn", "IfcCovering", "IfcCurtainWall", "IfcDoor",
    "IfcFooting", "IfcMember", "IfcPlate", "IfcRailing", "IfcRamp",
    "IfcRampFlight", "IfcRoof", "IfcShadingDevice", "IfcSlab", "IfcStair",
    "IfcStairFlight", "IfcWall", "IfcWallStandardCase", "IfcWindow",
])
_register("Shared Bldg Services", InterOperability, [
    "IfcDistributionChamberElement", "IfcDistributionElement",
    "IfcDistributionControlElement", "IfcDistributionFlowElement",
    "IfcEnergyConversionDevice", "IfcFlowController", "IfcFlowFitting",
    "IfcFlowMovingDevice", "IfcFlowSegment", "IfcFlowStorageDevice",
    "IfcFlowTerminal", "IfcFlowTreatmentDevice",
])
_register("Shared Components", InterOperability, [
    "IfcDiscreteAccessory", "IfcFastener", "IfcMechanicalFastener",
])
_register("Shared Facilities", InterOperability, [
    "IfcFurniture", "IfcSystemFurnitureElement", "IfcOccupant",
    "IfcRelAssignsToActor",
])
_register("Shared Management", InterOperability, [
    "IfcCostItem", "IfcCostSchedule", "IfcProjectOrder",
])

# --- Core layer -----------------------------------------------------------
_register("Kernel", Core, [
    "IfcRoot", "IfcObjectDefinition", "IfcObject", "IfcProduct",
    "IfcRelationship", "IfcRelAggregates", "IfcRelDefinesByProperties",
    "IfcRelAssociatesMaterial", "IfcPropertyDefinition", "IfcTypeObject",
    "IfcTypeProduct",
])
_register("Product", Core, [
    "IfcElement", "IfcSpatialStructureElement", "IfcSpatialElement",
    "IfcSite", "IfcBuilding", "IfcBuildingStorey", "IfcSpace",
    "IfcOpeningElement", "IfcVoidingFeature", "IfcProjectionElement",
    "IfcRelContainedInSpatialStructure", "IfcRelVoidsElement",
    "IfcRelFillsElement", "IfcAnnotation", "IfcGrid",
])
_register("Process", Core, ["IfcTask", "IfcProcedure", "IfcEvent", "IfcProcess"])
_register("Control", Core, [
    "IfcControl", "IfcPerformanceHistory", "IfcRelAssignsToControl",
])

# --- Resource layer -------------------------------------------------------
_register("Geometry", Resource, [
    "IfcCartesianPoint", "IfcDirection", "IfcVector", "IfcAxis2Placement2D",
    "IfcAxis2Placement3D", "IfcAxis1Placement", "IfcPolyline", "IfcLine",
    "IfcCircle", "IfcEllipse", "IfcTrimmedCurve", "IfcCompositeCurve",
    "IfcIndexedPolyCurve", "IfcCartesianPointList2D", "IfcCartesianPointList3D",
    "IfcCartesianTransformationOperator3D", "IfcBSplineCurve",
])
_register("Geometric Model", Resource, [
    "IfcExtrudedAreaSolid", "IfcRevolvedAreaSolid", "IfcSweptDiskSolid",
    "IfcSurfaceCurveSweptAreaSolid", "IfcFacetedBrep", "IfcAdvancedBrep",
    "IfcCsgSolid", "IfcBooleanResult", "IfcBooleanClippingResult",
    "IfcHalfSpaceSolid", "IfcPolygonalBoundedHalfSpace", "IfcBlock",
    "IfcTriangulatedFaceSet", "IfcPolygonalFaceSet", "IfcIndexedPolygonalFace",
    "IfcFaceBasedSurfaceModel", "IfcShellBasedSurfaceModel",
])
_register("Topology", Resource, [
    "IfcFace", "IfcFaceOuterBound", "IfcFaceBound", "IfcPolyLoop",
    "IfcClosedShell", "IfcOpenShell", "IfcConnectedFaceSet", "IfcVertexPoint",
    "IfcEdge", "IfcEdgeCurve", "IfcEdgeLoop",
])
_register("Profile", Resource, [
    "IfcRectangleProfileDef", "IfcRoundedRectangleProfileDef",
    "IfcCircleProfileDef", "IfcCircleHollowProfileDef", "IfcEllipseProfileDef",
    "IfcArbitraryClosedProfileDef", "IfcArbitraryProfileDefWithVoids",
    "IfcArbitraryOpenProfileDef", "IfcIShapeProfileDef", "IfcLShapeProfileDef",
    "IfcTShapeProfileDef", "IfcUShapeProfileDef", "IfcCShapeProfileDef",
    "IfcZShapeProfileDef", "IfcCompositeProfileDef", "IfcDerivedProfileDef",
])
_register("Representation", Resource, [
    "IfcProductRepresentation", "IfcProductDefinitionShape",
    "IfcRepresentation", "IfcShapeRepresentation", "IfcTopologyRepresentation",
    "IfcRepresentationContext", "IfcGeometricRepresentationContext",
    "IfcGeometricRepresentationSubContext", "IfcRepresentationItem",
    "IfcRepresentationMap", "IfcMappedItem", "IfcStyledItem", "IfcShapeAspect",
])
_register("Geometric Constraint", Resource, [
    "IfcObjectPlacement", "IfcLocalPlacement", "IfcGridPlacement",
])
_register("Measure", Resource, [
    "IfcSIUnit", "IfcConversionBasedUnit", "IfcUnitAssignment",
    "IfcMeasureWithUnit", "IfcDimensionalExponents", "IfcDerivedUnit",
])
_register("Material", Resource, [
    "IfcMaterial", "IfcMaterialLayer", "IfcMaterialLayerSet",
    "IfcMaterialLayerSetUsage", "IfcMaterialList", "IfcMaterialProfile",
])
_register("Presentation Appearance", Resource, [
    "IfcSurfaceStyle", "IfcSurfaceStyleRendering", "IfcSurfaceStyleShading",
    "IfcColourRgb", "IfcPresentationStyleAssignment",
])
_register("Property", Resource, [
    "IfcPropertySet", "IfcPropertySingleValue", "IfcPropertyEnumeratedValue",
    "IfcComplexProperty",
])
_register("Utility", Resource, ["IfcOwnerHistory", "IfcApplication", "IfcTable"])
_register("Actor", Resource, [
    "IfcPerson", "IfcOrganization", "IfcPersonAndOrganization", "IfcActorRole",
])
_register("External Reference", Resource, [
    "IfcClassification", "IfcClassificationReference", "IfcDocumentReference",
    "IfcLibraryReference",
])
_register("DateTime", Resource, ["IfcDateTime", "IfcDuration", "IfcTimePeriod"])
_register("Quantity", Resource, [
    "IfcElementQuantity", "IfcQuantityLength", "IfcQuantityArea",
    "IfcQuantityVolume", "IfcQuantityWeight", "IfcQuantityCount",
])


def classify(entity_name):
    """Return the Layer instance for an IFC entity name, or None.

    Falls back through the IFC inheritance chain is *not* attempted here on
    purpose: an unknown entity should announce itself rather than be silently
    filed under its parent's layer.
    """
    hit = _ENTITY_SCHEMA.get(entity_name.upper())
    if hit is None:
        return None
    schema, layer_cls = hit
    return layer_cls(schema)


def classify_instance(inst):
    """Classify an ifcopenshell entity instance, walking up its supertypes.

    Unlike classify(), this one does walk the inheritance chain, because a
    concrete instance genuinely is-a its supertype and inherits its layer.
    """
    direct = classify(inst.is_a())
    if direct is not None:
        return direct
    for parent in _supertypes(inst):
        found = classify(parent)
        if found is not None:
            return found
    return None


def _supertypes(inst):
    """Yield supertype names of an instance, nearest first."""
    try:
        decl = inst.wrapped_data.declaration()
    except Exception:
        return
    decl = getattr(decl, "as_entity", lambda: None)()
    if decl is None:
        return
    current = decl.supertype()
    while current is not None:
        yield current.name()
        current = current.supertype()
