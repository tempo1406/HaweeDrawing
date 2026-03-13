using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using HaweeDrawing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaweeDrawing.Services
{
    public interface IPipeCollectorService
    {
        List<PipeModel> GetAllPipes(Document doc);
        List<FittingModel> GetAllFittings(Document doc);
        List<PipeModel> GetPipesBySystem(Document doc, string systemName);
        List<string> GetSystemNames(Document doc);
        List<string> GetLevelNames(Document doc);
        List<PipeModel> GetPipesInActiveView(Document doc);
        ExportDataModel GetExportData(Document doc, bool filterByActiveView = false, string systemFilter = null, string levelFilter = null);
    }

    public class PipeCollectorService : IPipeCollectorService
    {
        private readonly IUnitConverter _unitConverter;

        public PipeCollectorService(IUnitConverter unitConverter)
        {
            _unitConverter = unitConverter ?? throw new ArgumentNullException(nameof(unitConverter));
        }

        public List<PipeModel> GetAllPipes(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe));

            return collector.Cast<Pipe>().Select(pipe => MapPipeToModel(pipe)).ToList();
        }

        public List<PipeModel> GetPipesBySystem(Document doc, string systemName)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (string.IsNullOrEmpty(systemName))
                return GetAllPipes(doc);

            var allPipes = GetAllPipes(doc);
            return allPipes.Where(p => p.SystemTypeName == systemName).ToList();
        }

        public List<string> GetSystemNames(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var pipes = GetAllPipes(doc);
            return pipes.Select(p => p.SystemTypeName)
                       .Distinct()
                       .OrderBy(s => s)
                       .ToList();
        }

        public List<string> GetLevelNames(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var pipes = GetAllPipes(doc);
            return pipes.Select(p => p.LevelName)
                       .Distinct()
                       .OrderBy(s => s)
                       .ToList();
        }

        public List<PipeModel> GetPipesInActiveView(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var activeView = doc.ActiveView;
            if (activeView == null)
                return GetAllPipes(doc);

            var collector = new FilteredElementCollector(doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe));

            return collector.Cast<Pipe>().Select(pipe => MapPipeToModel(pipe)).ToList();
        }

        private PipeModel MapPipeToModel(Pipe pipe)
        {
            var model = new PipeModel
            {
                Id = pipe.Id.Value.ToString(),
                LevelName = GetLevelName(pipe),
                SystemTypeName = GetSystemTypeName(pipe),
                PipeTypeName = GetPipeTypeName(pipe),
                Diameter = _unitConverter.FeetToMeters(GetDiameterInFeet(pipe))
            };

            GetPipeEndPoints(pipe, out XYZ start, out XYZ end);
            model.StartPoint = ConvertXYZToMeters(start);
            model.EndPoint = ConvertXYZToMeters(end);

            model.Connectors = GetPipeConnectors(pipe);

            return model;
        }

        private string GetSystemTypeName(Pipe pipe)
        {
            try
            {
                var systemTypeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (systemTypeParam != null && systemTypeParam.HasValue)
                {
                    var systemTypeId = systemTypeParam.AsElementId();
                    if (systemTypeId != null && systemTypeId != ElementId.InvalidElementId)
                    {
                        var systemType = pipe.Document.GetElement(systemTypeId);
                        if (systemType != null)
                            return systemType.Name;
                    }
                }

                var systemParam = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                if (systemParam != null && systemParam.HasValue)
                    return systemParam.AsString();

                return "No System";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetPipeTypeName(Pipe pipe)
        {
            try
            {
                var pipeType = pipe.Document.GetElement(pipe.GetTypeId());
                if (pipeType != null)
                    return pipeType.Name;
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private double GetDiameterInFeet(Pipe pipe)
        {
            try
            {
                var diameterParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParam != null && diameterParam.HasValue)
                {
                    return diameterParam.AsDouble();
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetLevelName(Pipe pipe)
        {
            try
            {
                var levelParam = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (levelParam != null && levelParam.HasValue)
                {
                    var levelId = levelParam.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        var level = pipe.Document.GetElement(levelId) as Level;
                        if (level != null)
                            return level.Name;
                    }
                }
                return "No Level";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void GetPipeEndPoints(Pipe pipe, out XYZ start, out XYZ end)
        {
            try
            {
                LocationCurve locationCurve = pipe.Location as LocationCurve;
                if (locationCurve != null)
                {
                    Curve curve = locationCurve.Curve;
                    start = curve.GetEndPoint(0);
                    end = curve.GetEndPoint(1);
                    return;
                }
            }
            catch { }

            start = XYZ.Zero;
            end = XYZ.Zero;
        }

        private XYZModel ConvertXYZToMeters(XYZ xyz)
        {
            if (xyz == null)
                return new XYZModel(0, 0, 0);

            return new XYZModel(
                _unitConverter.FeetToMeters(xyz.X),
                _unitConverter.FeetToMeters(xyz.Y),
                _unitConverter.FeetToMeters(xyz.Z)
            );
        }

        private List<ConnectorModel> GetPipeConnectors(Pipe pipe)
        {
            var connectorModels = new List<ConnectorModel>();

            try
            {
                var connectorSet = pipe.ConnectorManager?.Connectors;
                if (connectorSet == null)
                    return connectorModels;

                foreach (Connector connector in connectorSet)
                {
                    var connectorModel = new ConnectorModel
                    {
                        ConnectorId = connector.Id,
                        Origin = ConvertXYZToMeters(connector.Origin),
                        Diameter = _unitConverter.FeetToMeters(connector.Radius * 2),
                        ConnectedToId = GetConnectedElementId(connector)
                    };

                    connectorModels.Add(connectorModel);
                }
            }
            catch { }

            return connectorModels;
        }

        private string GetConnectedElementId(Connector connector)
        {
            try
            {
                if (connector.IsConnected)
                {
                    var connectorSet = connector.AllRefs;
                    foreach (Connector conn in connectorSet)
                    {
                        if (conn.Owner.Id != connector.Owner.Id)
                        {
                            return conn.Owner.Id.Value.ToString();
                        }
                    }
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        public List<FittingModel> GetAllFittings(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance));

            return collector.Cast<FamilyInstance>().Select(fitting => MapFittingToModel(fitting)).ToList();
        }

        private FittingModel MapFittingToModel(FamilyInstance fitting)
        {
            var model = new FittingModel
            {
                Id = fitting.Id.Value.ToString(),
                LevelName = GetLevelNameFromElement(fitting),
                FamilyName = fitting.Symbol?.Family?.Name ?? "Unknown",
                TypeName = fitting.Symbol?.Name ?? "Unknown",
                Angle = GetFittingAngle(fitting),
                Mirrored = fitting.Mirrored,
                HandFlipped = fitting.HandFlipped,
                FacingFlipped = fitting.FacingFlipped
            };

            var location = fitting.Location as LocationPoint;
            if (location != null)
            {
                model.LocationPoint = ConvertXYZToMeters(location.Point);
                model.Transform = GetTransformModel(fitting.GetTransform());
                ComputeFittingOrientation(fitting, model, location.Point);
            }

            model.Connectors = GetFittingConnectors(fitting);

            return model;
        }

        private string GetLevelNameFromElement(Element element)
        {
            try
            {
                var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (levelParam == null || !levelParam.HasValue)
                    levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                
                if (levelParam != null && levelParam.HasValue)
                {
                    var levelId = levelParam.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        var level = element.Document.GetElement(levelId) as Level;
                        if (level != null)
                            return level.Name;
                    }
                }
                return "No Level";
            }
            catch
            {
                return "Unknown";
            }
        }

        private double GetFittingAngle(FamilyInstance fitting)
        {
            try
            {
                var location = fitting.Location as LocationPoint;
                if (location != null)
                {
                    double radians = location.Rotation;
                    return radians * (180.0 / Math.PI);
                }
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private TransformModel GetTransformModel(Transform transform)
        {
            if (transform == null)
                return new TransformModel();

            return new TransformModel
            {
                Origin = ConvertXYZToMeters(transform.Origin),
                BasisX = new XYZModel(transform.BasisX.X, transform.BasisX.Y, transform.BasisX.Z),
                BasisY = new XYZModel(transform.BasisY.X, transform.BasisY.Y, transform.BasisY.Z),
                BasisZ = new XYZModel(transform.BasisZ.X, transform.BasisZ.Y, transform.BasisZ.Z)
            };
        }

        private void ComputeFittingOrientation(FamilyInstance fitting, FittingModel model, XYZ center)
        {
            try
            {
                var mepModel = fitting.MEPModel;
                if (mepModel == null) return;

                var connectorSet = mepModel.ConnectorManager?.Connectors;
                if (connectorSet == null) return;

                var endConnectors = new List<Connector>();
                foreach (Connector conn in connectorSet)
                {
                    if (conn.ConnectorType == ConnectorType.End)
                        endConnectors.Add(conn);
                }

                if (endConnectors.Count == 2)
                {
                    ComputeElbowOrientation(endConnectors, center, model);
                }
                else if (endConnectors.Count == 3)
                {
                    ComputeTeeOrientation(endConnectors, center, model);
                }
            }
            catch { }
        }

        /// Compute BasisX for 90° elbow 
        /// v1, v2 = direction vectors from center to each connector
        /// ? = atan2(vsum.Y, vsum.X), ? = 135°, ? = ? - ?
        /// BasisX = (cos(?), sin(?), 0)
        private void ComputeElbowOrientation(List<Connector> connectors, XYZ center, FittingModel model)
        {
            XYZ v1 = connectors[0].Origin - center;
            XYZ v2 = connectors[1].Origin - center;

            if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9) return;

            v1 = v1.Normalize();
            v2 = v2.Normalize();

            double dotProduct = v1.DotProduct(v2);
            if (Math.Abs(dotProduct) > 0.15) return;

            XYZ vsum = v1 + v2;
            if (vsum.GetLength() < 1e-9) return;

            double alphaDeg = Math.Atan2(vsum.Y, vsum.X) * (180.0 / Math.PI);

            double betaDeg = 135.0;

            double thetaDeg = alphaDeg - betaDeg;
            double thetaRad = thetaDeg * (Math.PI / 180.0);

            double bx = Math.Cos(thetaRad);
            double by = Math.Sin(thetaRad);

            model.Transform.BasisX = new XYZModel(bx, by, 0);
            model.Transform.BasisY = new XYZModel(-by, bx, 0);
            model.Transform.BasisZ = new XYZModel(0, 0, 1);
            model.Angle = Math.Atan2(by, bx) * (180.0 / Math.PI);
        }

        /// Compute BasisX for tee
        /// Find branch connector (the one not opposite to any other)
        /// BasisX = BasisZ × Vbranch = (0,0,1) × (vx,vy,0) = (-vy, vx, 0)
        private void ComputeTeeOrientation(List<Connector> connectors, XYZ center, FittingModel model)
        {
            var directions = new List<XYZ>();
            foreach (var conn in connectors)
            {
                XYZ dir = conn.Origin - center;
                if (dir.GetLength() < 1e-9) return;
                directions.Add(dir.Normalize());
            }

            int branchIndex = -1;
            for (int i = 0; i < 3; i++)
            {
                bool hasOpposite = false;
                for (int j = 0; j < 3; j++)
                {
                    if (i == j) continue;
                    if (directions[i].DotProduct(directions[j]) < -0.9)
                    {
                        hasOpposite = true;
                        break;
                    }
                }
                if (!hasOpposite)
                {
                    branchIndex = i;
                    break;
                }
            }

            if (branchIndex < 0) return;

            XYZ vBranch = directions[branchIndex];

            double bx = -vBranch.Y;
            double by = vBranch.X;

            model.Transform.BasisX = new XYZModel(bx, by, 0);
            model.Transform.BasisY = new XYZModel(-by, bx, 0);
            model.Transform.BasisZ = new XYZModel(0, 0, 1);
            model.Angle = Math.Atan2(by, bx) * (180.0 / Math.PI);
        }

        private List<ConnectorModel> GetFittingConnectors(FamilyInstance fitting)
        {
            var connectorModels = new List<ConnectorModel>();

            try
            {
                var mepModel = fitting.MEPModel;
                if (mepModel == null)
                    return connectorModels;

                var connectorSet = mepModel.ConnectorManager?.Connectors;
                if (connectorSet == null)
                    return connectorModels;

                foreach (Connector connector in connectorSet)
                {
                    var connectorModel = new ConnectorModel
                    {
                        ConnectorId = connector.Id,
                        Origin = ConvertXYZToMeters(connector.Origin),
                        Diameter = _unitConverter.FeetToMeters(connector.Radius * 2),
                        ConnectedToId = GetConnectedElementId(connector)
                    };

                    connectorModels.Add(connectorModel);
                }
            }
            catch { }

            return connectorModels;
        }

        public ExportDataModel GetExportData(Document doc, bool filterByActiveView = false, string systemFilter = null, string levelFilter = null)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var exportData = new ExportDataModel();

            if (filterByActiveView)
            {
                exportData.Pipes = GetPipesInActiveView(doc);
            }
            else
            {
                exportData.Pipes = GetAllPipes(doc);
            }

            if (!string.IsNullOrEmpty(systemFilter))
            {
                exportData.Pipes = exportData.Pipes.Where(p => p.SystemTypeName == systemFilter).ToList();
            }

            if (!string.IsNullOrEmpty(levelFilter))
            {
                exportData.Pipes = exportData.Pipes.Where(p => p.LevelName == levelFilter).ToList();
            }

            exportData.Fittings = GetAllFittings(doc);

            if (!string.IsNullOrEmpty(levelFilter))
            {
                exportData.Fittings = exportData.Fittings.Where(f => f.LevelName == levelFilter).ToList();
            }

            return exportData;
        }
    }
}
