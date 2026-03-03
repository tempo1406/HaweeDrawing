using System.Collections.Generic;

namespace HaweeDrawing.Models
{
    /// <summary>
    /// Represents the complete export data structure containing pipes and fittings
    /// </summary>
    public class ExportDataModel
    {
        public List<PipeModel> Pipes { get; set; }
        public List<FittingModel> Fittings { get; set; }

        public ExportDataModel()
        {
            Pipes = new List<PipeModel>();
            Fittings = new List<FittingModel>();
        }
    }
}
