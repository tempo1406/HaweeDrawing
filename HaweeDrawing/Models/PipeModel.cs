using System.Collections.Generic;

namespace HaweeDrawing.Models
{
    public class PipeModel
    {
        public string Id { get; set; }
        public string LevelName { get; set; }
        public string SystemTypeName { get; set; }
        public string PipeTypeName { get; set; }
        public double Diameter { get; set; }
        public XYZModel StartPoint { get; set; }
        public XYZModel EndPoint { get; set; }
        public List<ConnectorModel> Connectors { get; set; }

        public PipeModel()
        {
            StartPoint = new XYZModel();
            EndPoint = new XYZModel();
            Connectors = new List<ConnectorModel>();
        }
    }
}
