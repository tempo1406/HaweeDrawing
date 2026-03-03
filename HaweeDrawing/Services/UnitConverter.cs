namespace HaweeDrawing.Services
{
    public interface IUnitConverter
    {
        double FeetToMillimeters(double feet);
        double FeetToMeters(double feet);
        double MetersToFeet(double meters);
        double SquareFeetToSquareMillimeters(double squareFeet);
    }

    public class UnitConverter : IUnitConverter
    {
        private const double FEET_TO_MM = 304.8;
        private const double FEET_TO_M = 0.3048;

        public double FeetToMillimeters(double feet)
        {
            return feet * FEET_TO_MM;
        }

        public double FeetToMeters(double feet)
        {
            return feet * FEET_TO_M;
        }

        public double MetersToFeet(double meters)
        {
            return meters / FEET_TO_M;
        }

        public double SquareFeetToSquareMillimeters(double squareFeet)
        {
            return squareFeet * FEET_TO_MM * FEET_TO_MM;
        }
    }
}
