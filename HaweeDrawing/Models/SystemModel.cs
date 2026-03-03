using System.Collections.Generic;

namespace HaweeDrawing.Models
{
    public class SystemModel
    {
        public string SystemName { get; set; }
        public List<PipeModel> Pipes { get; set; }
        public int PipeCount { get; set; }

        public SystemModel()
        {
            Pipes = new List<PipeModel>();
        }
    }
}
