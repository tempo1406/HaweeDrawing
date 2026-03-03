using Newtonsoft.Json;
using System;

namespace HaweeDrawing.Models
{
    public class XYZModel
    {
        [JsonProperty("X")]
        public string X { get; set; }
        
        [JsonProperty("Y")]
        public string Y { get; set; }
        
        [JsonProperty("Z")]
        public string Z { get; set; }

        [JsonIgnore]
        private double _x;
        [JsonIgnore]
        private double _y;
        [JsonIgnore]
        private double _z;

        public XYZModel()
        {
            X = "0.0";
            Y = "0.0";
            Z = "0.0";
        }

        public XYZModel(double x, double y, double z)
        {
            _x = x;
            _y = y;
            _z = z;
            X = x.ToString("0.0################");
            Y = y.ToString("0.0################");
            Z = z.ToString("0.0################");
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }
    }
}
