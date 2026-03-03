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

            using (Transaction trans = new Transaction(doc, "Import Pipes and Fittings with Auto-Connect"))
            {
                trans.Start();

                try
                {
                    // Step 1: Load families from directory if provided
                    if (!string.IsNullOrEmpty(familyDirectory))
                    {
                        int familiesLoaded = _familyLoadService.LoadFamiliesFromDirectory(doc, familyDirectory);
                        System.Diagnostics.Debug.WriteLine($"Loaded {familiesLoaded} families from {familyDirectory}");
                    }

                    // Step 2: Create all pipes first
                    foreach (var pipeModel in exportData.Pipes)
                    {
                        try
                        {
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

                    // Step 3: Create all fittings
                    foreach (var fittingModel in exportData.Fittings)
                    {
                        try
                        {
                            var fittingResult = CreateFittingWithReason(doc, fittingModel, familyDirectory);
                            if (fittingResult.success && fittingResult.element != null)
                            {
                                fittingsCreated++;
                                _createdElements[fittingModel.Id] = fittingResult.element;
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

                    // Step 4: Auto-connect connectors
                    connectionsCreated = AutoConnectElements(doc, exportData);

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    throw new InvalidOperationException($"Failed to import data: {ex.Message}", ex);
                }
            }

            string resultMessage = $"? Import completed:\n" +
                           $"???????????????????????\n" +
                           $"?? Pipes: {pipesCreated} created, {pipesSkipped} skipped\n" +
                           $"?? Fittings: {fittingsCreated} created, {fittingsSkipped} skipped\n" +
                           $"?? Connections: {connectionsCreated} auto-connected\n" +
                           $"???????????????????????";

            if (skipReasons.Count > 0)
            {
                resultMessage += $"\n\n?? Skip Reasons (first 10):\n";
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

                    // Try to connect each connector
                    foreach (var connectorModel in pipeModel.Connectors)
                    {
                        if (string.IsNullOrEmpty(connectorModel.ConnectedToId))
                            continue;

                        if (!_createdElements.ContainsKey(connectorModel.ConnectedToId))
                            continue;

                        var targetElement = _createdElements[connectorModel.ConnectedToId];
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

                    foreach (var connectorModel in fittingModel.Connectors)
                    {
                        if (string.IsNullOrEmpty(connectorModel.ConnectedToId))
                            continue;

                        if (!_createdElements.ContainsKey(connectorModel.ConnectedToId))
                            continue;

                        var targetElement = _createdElements[connectorModel.ConnectedToId];
                        if (ConnectElements(fitting, targetElement, connectorModel))
                            connectionCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error auto-connecting: {ex.Message}");
            }

            return connectionCount;
        }

        private bool ConnectElements(Element element1, Element element2, ConnectorModel connectorInfo)
        {
            try
            {
                var connector1 = FindClosestConnector(element1, connectorInfo.Origin);
                var connector2 = FindClosestConnector(element2, null);

                if (connector1 == null || connector2 == null)
                    return false;

                if (connector1.IsConnected || connector2.IsConnected)
                {
                    // Check if already connected to each other
                    var refs = connector1.AllRefs;
                    foreach (Connector conn in refs)
                    {
                        if (conn.Owner.Id == element2.Id)
                            return false; // Already connected
                    }
                }

                // Connect the connectors
                connector1.ConnectTo(connector2);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to connect elements: {ex.Message}");
                return false;
            }
        }

        private Connector FindClosestConnector(Element element, XYZModel targetOrigin)
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

            XYZ targetPoint = targetOrigin != null ? ConvertToRevitXYZ(targetOrigin) : null;

            Connector closestConnector = null;
            double minDistance = double.MaxValue;

            foreach (Connector connector in connectorSet)
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
                    // Return first available unconnected connector
                    if (!connector.IsConnected)
                        return connector;
                }
            }

            return closestConnector;
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

                // Get level or use first available level
                var level = GetLevel(doc, pipeModel.LevelName);
                if (level == null)
                {
                    level = GetFirstAvailableLevel(doc);
                    if (level == null)
                        return (false, "No levels found in document", null);
                    
                    System.Diagnostics.Debug.WriteLine($"Level '{pipeModel.LevelName}' not found, using '{level.Name}' instead");
                }

                var systemType = GetSystemType(doc, pipeModel.SystemTypeName);

                XYZ startPoint = ConvertToRevitXYZ(pipeModel.StartPoint);
                XYZ endPoint = ConvertToRevitXYZ(pipeModel.EndPoint);

                if (startPoint.DistanceTo(endPoint) < 0.001)
                    return (false, "Start and end points are too close", null);

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
                // Try to get or load family symbol
                var fittingSymbol = _familyLoadService.GetOrLoadFamilySymbol(
                    doc, fittingModel.FamilyName, fittingModel.TypeName, familyDirectory);

                if (fittingSymbol == null)
                    return (false, $"Fitting not found and could not be loaded: '{fittingModel.FamilyName} - {fittingModel.TypeName}'", null);

                // Get level or use first available level
                var level = GetLevel(doc, fittingModel.LevelName);
                if (level == null)
                {
                    level = GetFirstAvailableLevel(doc);
                    if (level == null)
                        return (false, "No levels found in document", null);
                    
                    System.Diagnostics.Debug.WriteLine($"Level '{fittingModel.LevelName}' not found, using '{level.Name}' instead");
                }

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
                        LocationPoint locationPoint = fitting.Location as LocationPoint;
                        if (locationPoint != null)
                        {
                            double radians = fittingModel.Angle * (Math.PI / 180.0);
                            XYZ axis = XYZ.BasisZ;
                            locationPoint.Rotate(Line.CreateBound(location, location + axis), radians);
                        }
                    }

                    if (fittingModel.Mirrored || fittingModel.HandFlipped || fittingModel.FacingFlipped)
                    {
                        if (fittingModel.Mirrored && fitting.CanFlipHand)
                            fitting.flipHand();

                        if (fittingModel.HandFlipped && fitting.CanFlipHand)
                            fitting.flipHand();

                        if (fittingModel.FacingFlipped && fitting.CanFlipFacing)
                            fitting.flipFacing();
                    }

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
    }
}
