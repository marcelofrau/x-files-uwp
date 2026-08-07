using System;

namespace XFiles.Controls
{
    /// <summary>
    /// Pure geometry math for the Win98-style isometric disk pie. No UWP types —
    /// unit-testable on desktop. The dialog builds PathGeometry from these values.
    /// </summary>
    public static class PieGeometry
    {
        /// <summary>
        /// Computes the two pie slices for a used fraction (0..1). First slice = used,
        /// second = free. Angles in degrees, clockwise, starting at 12 o'clock. A slice
        /// with sweep 0 is omitted (single full-circle slice when used is 0 or 1).
        /// </summary>
        public static (double Fraction, double StartDeg, double EndDeg)[] Slices(double usedFraction)
        {
            double used = Math.Max(0, Math.Min(1, usedFraction));

            if (used <= 0)
                return new[] { (1.0, 0.0, 360.0) };

            if (used >= 1)
                return new[] { (1.0, 0.0, 360.0) };

            double sweep = used * 360.0;
            return new[]
            {
                (used, 0.0, sweep),
                (1.0 - used, sweep, 360.0)
            };
        }

        /// <summary>
        /// Point on a circle (cx,cy,radius) at the given angle in degrees. 0° = 12 o'clock,
        /// increasing clockwise (screen coords, +Y down).
        /// </summary>
        public static (double X, double Y) ArcPoint(double cx, double cy, double radius, double angleDeg)
        {
            double rad = (angleDeg - 90.0) * Math.PI / 180.0;
            return (cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
        }

        /// <summary>
        /// Whether an arc spanning [start,end] is greater than 180° (needs large-arc flag).
        /// </summary>
        public static bool IsLargeArc(double startDeg, double endDeg)
        {
            return (endDeg - startDeg) > 180.0;
        }
    }
}
