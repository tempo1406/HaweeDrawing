namespace HaweeDrawing.Models
{
    public class ConnectorModel
    {
        public int ConnectorId { get; set; }
        public XYZModel Origin { get; set; }
        public double Diameter { get; set; }
        public string ConnectedToId { get; set; }

        public ConnectorModel()
        {
            Origin = new XYZModel();
        }
    }
}
