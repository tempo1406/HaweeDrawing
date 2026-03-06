using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using HaweeDrawing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaweeDrawing.Services
{
    public interface IPipeCreationServiceEnhanced
    {
        string CreateFromExportData(Document doc, ExportDataModel exportData, string familyDirectory);
        string ValidateImportData(Document doc, ExportDataModel exportData);
    }

    public class PipeCreationServiceEnhanced : IPipeCreationServiceEnhanced
    {
        private readonly IUnitConverter _unitConverter;
        private readonly IFamilyLoadService _familyLoadService;
        private Dictionary<string, Element> _createdElements;
        private List<XYZ> _createdFittingLocations;
        private const double LOCATION_TOLERANCE = 0.00328; // 1mm tolerance for duplicate detection

        public PipeCreationServiceEnhanced(IUnitConverter unitConverter, IFamilyLoadService familyLoadService)
        {
            _unitConverter = unitConverter ?? throw new ArgumentNullException(nameof(unitConverter));
            _familyLoadService = familyLoadService ?? throw new ArgumentNullException(nameof(familyLoadService));
        }

        public string CreateFromExportData(Document doc, ExportDataModel exportData, string familyDirectory)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (exportData == null)
                throw new ArgumentNullException(nameof(exportData));

            int pipesCreated = 0;
            int fittingsCreated = 0;
            int pipesSkipped = 0;
            int fittingsSkipped = 0;
            int connectionsCreated = 0;
            var skipReasons = new List<string>();

            _createdElements = new Dictionary<string, Element>();
            _createdFittingLocations = new List<XYZ>();

            using (Transaction trans = new Transaction(doc, "Import Pipes and Fittings"))
            {
                trans.Start();

                try
                {
                    if (!string.IsNullOrEmpty(familyDirectory))
                    {
                        _familyLoadService.LoadFamiliesFromDirectory(doc, familyDirectory);
                    }

                    foreach (var pipeModel in exportData.Pipes)
                    {
                        try
                        {
                            // Check for duplicate ID
                            if (_createdElements.ContainsKey(pipeModel.Id))
                            {
                                pipesSkipped++;
                                skipReasons.Add($"Pipe {pipeModel.Id}: Duplicate ID detected, skipping");
                                continue;
                            }

                            var pipeResult = CreatePipeWithReason(doc, pipeModel, familyDirectory);
                            if (pipeResult.success && pipeResult.element != null)
                            {
                                pipesCreated++;
                                _createdElements[pipeModel.Id] = pipeResult.element;
                            }
                            else
                            {
                                pipesSkipped++;
                                if (!string.IsNullOrEmpty(pipeResult.reason))
                                    skipReasons.Add($"Pipe {pipeModel.Id}: {pipeResult.reason}");
                            }
                        }
                        catch (Exception ex)
                        {
                            pipesSkipped++;
                            skipReasons.Add($"Pipe {pipeModel.Id}: Exception - {ex.Message}");
                        }
                    }


                    foreach (var fittingModel in exportData.Fittings)
                    {
                        try
                        {
                            // Check for duplicate ID
                            if (_createdElements.ContainsKey(fittingModel.Id))
                            {
                                fittingsSkipped++;
                                skipReasons.Add($"Fitting {fittingModel.Id}: Duplicate ID detected, skipping");
                                continue;
                            }

                            // Check for duplicate location
                            XYZ fittingLocation = ConvertToRevitXYZ(fittingModel.LocationPoint);
                            if (IsLocationOccupied(fittingLocation))
                            {
                                fittingsSkipped++;
                                skipReasons.Add($"Fitting {fittingModel.Id}: Duplicate location ({fittingLocation.X:F3}, {fittingLocation.Y:F3}, {fittingLocation.Z:F3}), skipping");
                                continue;
                            }

                            var fittingResult = CreateFittingWithReason(doc, fittingModel, familyDirectory);
                            if (fittingResult.success && fittingResult.element != null)
                            {
                                fittingsCreated++;
                                _createdElements[fittingModel.Id] = fittingResult.element;
                                _createdFittingLocations.Add(fittingLocation);
                            }
                            else
                            {
                                fittingsSkipped++;
                                if (!string.IsNullOrEmpty(fittingResult.reason))
                                    skipReasons.Add($"Fitting {fittingModel.Id}: {fittingResult.reason}");
                            }
                        }
                        catch (Exception ex)
                        {
                            fittingsSkipped++;
                            skipReasons.Add($"Fitting {fittingModel.Id}: Exception - {ex.Message}");
                        }
                    }

                    doc.Regenerate();

                    DeleteAutoGeneratedFittings(doc);

                    connectionsCreated = AutoConnectElements(doc, exportData);

                    // Set failure handling to continue on direction warnings
                    var failureOptions = trans.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(new PipeDirectionFailureHandler());
                    trans.SetFailureHandlingOptions(failureOptions);

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    throw new InvalidOperationException($"Failed to import data: {ex.Message}", ex);
                }
            }

            string resultMessage = $"Import completed:\n" +
                           $"???????????????????????\n" +
                           $"Pipes: {pipesCreated} created, {pipesSkipped} skipped\n" +
                           $"Fittings: {fittingsCreated} created, {fittingsSkipped} skipped\n" +
                           $"Connections: {connectionsCreated} auto-connected\n" +
                           $"???????????????????????";

            if (skipReasons.Count > 0)
            {
                resultMessage += $"\n\nSkip Reasons (first 10):\n";
                resultMessage += string.Join("\n", skipReasons.Take(10));
                if (skipReasons.Count > 10)
                    resultMessage += $"\n... and {skipReasons.Count - 10} more";
            }

            return resultMessage;
        }

        private int AutoConnectElements(Document doc, ExportDataModel exportData)
        {
            int connectionCount = 0;

            try
            {
                // Connect pipes to fittings based on connector information
                foreach (var pipeModel in exportData.Pipes)
                {
                    if (!_createdElements.ContainsKey(pipeModel.Id))
                        continue;

                    var pipe = _createdElements[pipeModel.Id] as Pipe;
                    if (pipe == null)
                        continue;

                    // Validate element is still valid
                    if (!IsElementValid(doc, pipe))
                    {
                        _createdElements.Remove(pipeModel.Id);
                        continue;
                    }

                    // Try to connect each connector
                    foreach (var connectorModel in pipeModel.Connectors)
                    {
                        if (string.IsNullOrEmpty(connectorModel.ConnectedToId))
                            continue;

                        if (!_createdElements.ContainsKey(connectorModel.ConnectedToId))
                            continue;

                        var targetElement = _createdElements[connectorModel.ConnectedToId];
                        if (!IsElementValid(doc, targetElement))
                        {
                            _createdElements.Remove(connectorModel.ConnectedToId);
                            continue;
                        }

                        if (ConnectElements(pipe, targetElement, connectorModel))
                            connectionCount++;
                    }
                }

                // Connect fittings
                foreach (var fittingModel in exportData.Fittings)
                {
                    if (!_createdElements.ContainsKey(fittingModel.Id))
                        continue;

                    var fitting = _createdElements[fittingModel.Id] as FamilyInstance;
                    if (fitting == null)
                        continue;

                    // Validate element is still valid
                    if (!IsElementValid(doc, fitting))
                    {
                        _createdElements.Remove(fittingModel.Id);
                        continue;
                    }

                    foreach (var connectorModel in fittingModel.Connectors)
                    {
                        if (string.IsNullOrEmpty(connectorModel.ConnectedToId))
                            continue;

                        if (!_createdElements.ContainsKey(connectorModel.ConnectedToId))
                            continue;

                        var targetElement = _createdElements[connectorModel.ConnectedToId];
                        if (!IsElementValid(doc, targetElement))
                        {
                            _createdElements.Remove(connectorModel.ConnectedToId);
                            continue;
                        }

                        if (ConnectElements(fitting, targetElement, connectorModel))
                            connectionCount++;
                    }
                }
            }
            catch
            {
            }

            return connectionCount;
        }

        private bool IsElementValid(Document doc, Element element)
        {
            try
            {
                var id = element.Id;
                return doc.GetElement(id) != null;
            }
            catch
            {
                return false;
            }
        }

        private bool ConnectElements(Element element1, Element element2, ConnectorModel connectorInfo)
        {
            try
            {
                if (element1 == null || element2 == null)
                    return false;

                try
                {
                    var _ = element1.Id;
                    var __ = element2.Id;
                }
                catch (Autodesk.Revit.Exceptions.InvalidObjectException)
                {
                    return false;
                }

                var connector1 = FindClosestConnector(element1, connectorInfo.Origin);
                if (connector1 == null)
                    return false;

                var connector2 = FindClosestConnectorByPoint(element2, connector1.Origin);
                if (connector2 == null)
                    return false;

                if (connector1.IsConnected || connector2.IsConnected)
                    return false;

                if (!AreConnectorsCompatible(connector1, connector2))
                    return false;

                connector1.ConnectTo(connector2);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.InvalidObjectException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private Connector FindClosestConnector(Element element, XYZModel targetOrigin)
        {
            XYZ targetPoint = targetOrigin != null ? ConvertToRevitXYZ(targetOrigin) : null;
            return FindClosestConnectorByPoint(element, targetPoint);
        }

        private Connector FindClosestConnectorByPoint(Element element, XYZ targetPoint)
        {
            ConnectorSet connectorSet = null;

            if (element is Pipe pipe)
            {
                connectorSet = pipe.ConnectorManager?.Connectors;
            }
            else if (element is FamilyInstance fitting)
            {
                connectorSet = fitting.MEPModel?.ConnectorManager?.Connectors;
            }

            if (connectorSet == null)
                return null;

            Connector closestConnector = null;
            double minDistance = double.MaxValue;

            foreach (Connector connector in connectorSet)
            {
                if (connector.ConnectorType != ConnectorType.End)
                    continue;

                if (!connector.IsConnected)
                {
                    if (targetPoint != null)
                    {
                        double distance = connector.Origin.DistanceTo(targetPoint);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestConnector = connector;
                        }
                    }
                    else
                    {
                        return connector;
                    }
                }
            }

            return closestConnector;
        }

        private bool AreConnectorsCompatible(Connector conn1, Connector conn2)
        {
            if (conn1 == null || conn2 == null)
                return false;

            // Check if both are End connectors
            if (conn1.ConnectorType != ConnectorType.End || conn2.ConnectorType != ConnectorType.End)
                return false;

            // Check if they are the same domain (Piping, HVAC, etc.)
            if (conn1.Domain != conn2.Domain)
                return false;

            const double TOLERANCE = 0.00328; // 1mm in feet
            if (conn1.Origin.DistanceTo(conn2.Origin) > TOLERANCE)
                return false;

            return true;
        }

        public string ValidateImportData(Document doc, ExportDataModel exportData)
        {
            if (doc == null || exportData == null)
                return "Invalid parameters";

            var issues = new List<string>();

            // Check levels
            var requiredLevels = new HashSet<string>();
            if (exportData.Pipes != null)
            {
                foreach (var pipe in exportData.Pipes)
                {
                    if (!string.IsNullOrEmpty(pipe.LevelName))
                        requiredLevels.Add(pipe.LevelName);
                }
            }
            if (exportData.Fittings != null)
            {
                foreach (var fitting in exportData.Fittings)
                {
                    if (!string.IsNullOrEmpty(fitting.LevelName))
                        requiredLevels.Add(fitting.LevelName);
                }
            }

            var missingLevels = new List<string>();
            foreach (var levelName in requiredLevels)
            {
                if (GetLevel(doc, levelName) == null)
                    missingLevels.Add(levelName);
            }

            if (missingLevels.Count > 0)
                issues.Add($"? Missing Levels: {string.Join(", ", missingLevels)}");

            // Check pipe types
            var requiredPipeTypes = new HashSet<string>();
            if (exportData.Pipes != null)
            {
                foreach (var pipe in exportData.Pipes)
                {
                    if (!string.IsNullOrEmpty(pipe.PipeTypeName))
                        requiredPipeTypes.Add(pipe.PipeTypeName);
                }
            }

            var missingPipeTypes = new List<string>();
            foreach (var typeName in requiredPipeTypes)
            {
                if (GetPipeType(doc, typeName) == null)
                    missingPipeTypes.Add(typeName);
            }

            if (missingPipeTypes.Count > 0)
                issues.Add($"? Missing Pipe Types: {string.Join(", ", missingPipeTypes)}");

            // Note: We don't check fitting families here since they can be auto-loaded

            if (issues.Count == 0)
                return "? All required elements are available!\n?? Fitting families will be auto-loaded if needed.";

            return string.Join("\n", issues);
        }

        private (bool success, string reason, Element element) CreatePipeWithReason(Document doc, PipeModel pipeModel, string familyDirectory)
        {
            try
            {
                var pipeType = GetPipeType(doc, pipeModel.PipeTypeName);
                if (pipeType == null)
                    return (false, $"PipeType not found: '{pipeModel.PipeTypeName}'", null);

                var level = GetLevel(doc, pipeModel.LevelName) ?? GetFirstAvailableLevel(doc);
                if (level == null)
                    return (false, "No levels found in document", null);

                var systemType = GetSystemType(doc, pipeModel.SystemTypeName);

                XYZ startPoint = ConvertToRevitXYZ(pipeModel.StartPoint);
                XYZ endPoint = ConvertToRevitXYZ(pipeModel.EndPoint);

                // Check minimum pipe length (1mm = 0.00328 feet)
                double pipeLength = startPoint.DistanceTo(endPoint);
                const double MIN_PIPE_LENGTH = 0.00328; // 1mm in feet
                
                if (pipeLength < MIN_PIPE_LENGTH)
                {
                    return (false, $"Pipe too short: {pipeLength:F6} ft (minimum: {MIN_PIPE_LENGTH} ft / 1mm)", null);
                }

                // Create pipe - Revit may swap start/end points automatically
                Pipe pipe = null;
                try
                {
                    pipe = Pipe.Create(doc, systemType?.Id ?? ElementId.InvalidElementId,
                                           pipeType.Id, level.Id, startPoint, endPoint);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // If creation fails, try swapping start and end points
                    try
                    {
                        pipe = Pipe.Create(doc, systemType?.Id ?? ElementId.InvalidElementId,
                                               pipeType.Id, level.Id, endPoint, startPoint);
                    }
                    catch
                    {
                        return (false, "Failed to create pipe with both point orders", null);
                    }
                }

                if (pipe != null)
                {
                    var diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (diameterParam != null && !diameterParam.IsReadOnly)
                    {
                        double diameterInFeet = _unitConverter.MetersToFeet(pipeModel.Diameter);
                        diameterParam.Set(diameterInFeet);
                    }

                    return (true, null, pipe);
                }

                return (false, "Pipe.Create returned null", null);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", null);
            }
        }

        private (bool success, string reason, Element element) CreateFittingWithReason(Document doc, FittingModel fittingModel, string familyDirectory)
        {
            try
            {
                var fittingSymbol = _familyLoadService.GetOrLoadFamilySymbol(
                    doc, fittingModel.FamilyName, fittingModel.TypeName, familyDirectory);

                if (fittingSymbol == null)
                    return (false, $"Fitting not found: '{fittingModel.FamilyName} - {fittingModel.TypeName}'", null);

                var level = GetLevel(doc, fittingModel.LevelName) ?? GetFirstAvailableLevel(doc);
                if (level == null)
                    return (false, "No levels found in document", null);

                if (!fittingSymbol.IsActive)
                {
                    fittingSymbol.Activate();
                    doc.Regenerate();
                }

                XYZ location = ConvertToRevitXYZ(fittingModel.LocationPoint);

                FamilyInstance fitting = doc.Create.NewFamilyInstance(
                    location,
                    fittingSymbol,
                    level,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                if (fitting != null)
                {
                    if (Math.Abs(fittingModel.Angle) > 0.001)
                    {
                        var locationPoint = fitting.Location as LocationPoint;
                        if (locationPoint != null)
                        {
                            double radians = fittingModel.Angle * (Math.PI / 180.0);
                            locationPoint.Rotate(Line.CreateBound(location, location + XYZ.BasisZ), radians);
                        }
                    }

                    if (fittingModel.Mirrored && fitting.CanFlipHand)
                        fitting.flipHand();
                    if (fittingModel.HandFlipped && fitting.CanFlipHand)
                        fitting.flipHand();
                    if (fittingModel.FacingFlipped && fitting.CanFlipFacing)
                        fitting.flipFacing();

                    return (true, null, fitting);
                }

                return (false, "NewFamilyInstance returned null", null);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", null);
            }
        }

        private PipeType GetPipeType(Document doc, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>();

            return collector.FirstOrDefault(pt => pt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        }

        private Level GetLevel(Document doc, string levelName)
        {
            if (string.IsNullOrEmpty(levelName) || levelName == "No Level" || levelName == "Unknown")
                return null;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>();

            return collector.FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
        }

        private Level GetFirstAvailableLevel(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation);

            return collector.FirstOrDefault();
        }

        private MEPSystemType GetSystemType(Document doc, string systemTypeName)
        {
            if (string.IsNullOrEmpty(systemTypeName) || systemTypeName == "No System" || systemTypeName == "Unknown")
                return null;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystemType))
                .Cast<MEPSystemType>();

            return collector.FirstOrDefault(st => st.Name.Equals(systemTypeName, StringComparison.OrdinalIgnoreCase));
        }

        private XYZ ConvertToRevitXYZ(XYZModel model)
        {
            if (model == null)
                return XYZ.Zero;

            double x = ParseCoordinate(model.X);
            double y = ParseCoordinate(model.Y);
            double z = ParseCoordinate(model.Z);

            return new XYZ(
                _unitConverter.MetersToFeet(x),
                _unitConverter.MetersToFeet(y),
                _unitConverter.MetersToFeet(z)
            );
        }

        private double ParseCoordinate(string coordinate)
        {
            if (string.IsNullOrEmpty(coordinate))
                return 0.0;

            if (double.TryParse(coordinate, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return 0.0;
        }

        private bool IsLocationOccupied(XYZ location)
        {
            if (location == null || _createdFittingLocations == null)
                return false;

            foreach (var existingLocation in _createdFittingLocations)
            {
                double distance = location.DistanceTo(existingLocation);
                if (distance < LOCATION_TOLERANCE)
                    return true;
            }

            return false;
        }

        private int DeleteAutoGeneratedFittings(Document doc)
        {
            int deletedCount = 0;

            try
            {
                // Build a HashSet of our created element IDs for fast and correct lookup
                // (Cannot use ContainsValue with Element reference - FilteredElementCollector
                //  returns new wrapper objects, so reference comparison always fails)
                var createdElementIds = new HashSet<ElementId>();
                foreach (var kvp in _createdElements)
                {
                    try
                    {
                        createdElementIds.Add(kvp.Value.Id);
                    }
                    catch
                    {
                        // Element already invalid, skip
                    }
                }

                // Find all fittings in the document
                var allFittings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeFitting)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                var fittingsToDelete = new List<ElementId>();

                foreach (var fitting in allFittings)
                {
                    if (createdElementIds.Contains(fitting.Id))
                        continue;

                    var location = (fitting.Location as LocationPoint)?.Point;
                    if (location != null)
                    {
                        foreach (var createdLocation in _createdFittingLocations)
                        {
                            if (location.DistanceTo(createdLocation) < LOCATION_TOLERANCE)
                            {
                                fittingsToDelete.Add(fitting.Id);
                                break;
                            }
                        }
                    }
                }

                // Delete the auto-generated fittings
                if (fittingsToDelete.Count > 0)
                {
                    doc.Delete(fittingsToDelete);
                    deletedCount = fittingsToDelete.Count;
                }

                // After deletion, validate _createdElements and remove any that were
                // cascade-deleted by Revit (e.g., elements connected to deleted fittings)
                var invalidKeys = new List<string>();
                foreach (var kvp in _createdElements)
                {
                    try
                    {
                        var id = kvp.Value.Id;
                        if (doc.GetElement(id) == null)
                        {
                            invalidKeys.Add(kvp.Key);
                        }
                    }
                    catch
                    {
                        invalidKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in invalidKeys)
                {
                    _createdElements.Remove(key);
                }
            }
            catch
            {
            }

            return deletedCount;
        }
    }

    /// <summary>
    /// Failure handler that automatically resolves "pipe/duct opposite direction" warnings
    /// by deleting the problematic elements rather than blocking the transaction.
    /// </summary>
    internal class PipeDirectionFailureHandler : Autodesk.Revit.DB.IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failureMessages = failuresAccessor.GetFailureMessages();

            foreach (var msg in failureMessages)
            {
                var severity = msg.GetSeverity();

                if (severity == FailureSeverity.Warning)
                {
                    // Dismiss all warnings (including direction warnings)
                    failuresAccessor.DeleteWarning(msg);
                }
                else if (severity == FailureSeverity.Error)
                {
                    // For errors, try to resolve by deleting the offending elements
                    if (msg.HasResolutions())
                    {
                        failuresAccessor.ResolveFailure(msg);
                    }
                    else
                    {
                        // Delete elements causing the error
                        var failingIds = msg.GetFailingElementIds();
                        if (failingIds.Count > 0)
                        {
                            failuresAccessor.DeleteElements(failingIds.ToList());
                        }
                    }
                    return FailureProcessingResult.ProceedWithCommit;
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
