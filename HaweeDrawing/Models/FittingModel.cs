using System.Collections.Generic;

namespace HaweeDrawing.Models
{
    public class FittingModel
    {
        public string Id { get; set; }
        public string LevelName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public XYZModel LocationPoint { get; set; }
        public double Angle { get; set; }
        public TransformModel Transform { get; set; }
        public List<ConnectorModel> Connectors { get; set; }
        public bool Mirrored { get; set; }
        public bool HandFlipped { get; set; }
        public bool FacingFlipped { get; set; }

        public FittingModel()
        {
            LocationPoint = new XYZModel();
            Transform = new TransformModel();
            Connectors = new List<ConnectorModel>();
        }
    }
}
