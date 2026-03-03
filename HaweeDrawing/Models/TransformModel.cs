namespace HaweeDrawing.Models
{
    public class TransformModel
    {
        public XYZModel Origin { get; set; }
        public XYZModel BasisX { get; set; }
        public XYZModel BasisY { get; set; }
        public XYZModel BasisZ { get; set; }

        public TransformModel()
        {
            Origin = new XYZModel();
            BasisX = new XYZModel();
            BasisY = new XYZModel();
            BasisZ = new XYZModel();
        }
    }
}
