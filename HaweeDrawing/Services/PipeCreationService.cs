using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using HaweeDrawing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaweeDrawing.Services
{

    public interface IPipeCreationService
    {
        string CreateFromExportData(Document doc, ExportDataModel exportData);
    }

    public class PipeCreationService : IPipeCreationService
    {
        private readonly IUnitConverter _unitConverter;

        public PipeCreationService(IUnitConverter unitConverter)
       {
            _unitConverter = unitConverter ?? throw new ArgumentNullException(nameof(unitConverter));
        }

        public string CreateFromExportData(Document doc, ExportDataModel exportData)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (exportData == null)
                throw new ArgumentNullException(nameof(exportData));

            int pipesCreated = 0;
            int fittingsCreated = 0;
            int pipesSkipped = 0;
            int fittingsSkipped = 0;

            using (Transaction trans = new Transaction(doc, "Import Pipes and Fittings"))
            {
                trans.Start();

                try
                {
                    foreach (var pipeModel in exportData.Pipes)
                    {
                        try
                        {
                            if (CreatePipe(doc, pipeModel))
                                pipesCreated++;
                            else
                                pipesSkipped++;
                        }
                        catch (Exception ex)
                        {
                            pipesSkipped++;
                            System.Diagnostics.Debug.WriteLine($"Failed to create pipe {pipeModel.Id}: {ex.Message}");
                        }
                    }

                    foreach (var fittingModel in exportData.Fittings)
                    {
                        try
                        {
                            if (CreateFitting(doc, fittingModel))
                                fittingsCreated++;
                            else
                                fittingsSkipped++;
                        }
                        catch (Exception ex)
                        {
                            fittingsSkipped++;
                            System.Diagnostics.Debug.WriteLine($"Failed to create fitting {fittingModel.Id}: {ex.Message}");
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    throw new InvalidOperationException($"Failed to import data: {ex.Message}", ex);
                }
            }

            return $"Import completed:\n" +
                   $"Pipes: {pipesCreated} created, {pipesSkipped} skipped\n" +
                   $"Fittings: {fittingsCreated} created, {fittingsSkipped} skipped";
        }

        private bool CreatePipe(Document doc, PipeModel pipeModel)
        {
            try
            {
                var pipeType = GetPipeType(doc, pipeModel.PipeTypeName);
                if (pipeType == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Pipe type not found: {pipeModel.PipeTypeName}");
                    return false;
                }

                var level = GetLevel(doc, pipeModel.LevelName);
                if (level == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Level not found: {pipeModel.LevelName}");
                    return false;
                }

                var systemType = GetSystemType(doc, pipeModel.SystemTypeName);

                XYZ startPoint = ConvertToRevitXYZ(pipeModel.StartPoint);
                XYZ endPoint = ConvertToRevitXYZ(pipeModel.EndPoint);

                Pipe pipe = Pipe.Create(doc, systemType?.Id ?? ElementId.InvalidElementId, 
                                       pipeType.Id, level.Id, startPoint, endPoint);

                if (pipe != null)
                {
                    var diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (diameterParam != null && !diameterParam.IsReadOnly)
                    {
                        double diameterInFeet = _unitConverter.MetersToFeet(pipeModel.Diameter);
                        diameterParam.Set(diameterInFeet);
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating pipe: {ex.Message}");
                return false;
            }
        }

        private bool CreateFitting(Document doc, FittingModel fittingModel)
        {
            try
            {
                var fittingSymbol = GetFittingSymbol(doc, fittingModel.FamilyName, fittingModel.TypeName);
                if (fittingSymbol == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Fitting type not found: {fittingModel.FamilyName} - {fittingModel.TypeName}");
                    return false;
                }

                var level = GetLevel(doc, fittingModel.LevelName);
                if (level == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Level not found: {fittingModel.LevelName}");
                    return false;
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

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating fitting: {ex.Message}");
                return false;
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

        private MEPSystemType GetSystemType(Document doc, string systemTypeName)
        {
            if (string.IsNullOrEmpty(systemTypeName) || systemTypeName == "No System" || systemTypeName == "Unknown")
                return null;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystemType))
                .Cast<MEPSystemType>();

            return collector.FirstOrDefault(st => st.Name.Equals(systemTypeName, StringComparison.OrdinalIgnoreCase));
        }

        private FamilySymbol GetFittingSymbol(Document doc, string familyName, string typeName)
        {
            if (string.IsNullOrEmpty(familyName) || string.IsNullOrEmpty(typeName))
                return null;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .Cast<FamilySymbol>();

            var exactMatch = collector.FirstOrDefault(fs => 
                fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
                return exactMatch;

            var familyMatch = collector.FirstOrDefault(fs => 
                fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

            return familyMatch;
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
