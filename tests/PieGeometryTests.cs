using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Controls;

namespace XFiles.Tests
{
    [TestClass]
    public class PieGeometryTests
    {
        [TestMethod]
        public void Slices_HalfUsed_ReturnsTwoSlices()
        {
            var slices = PieGeometry.Slices(0.5);
            Assert.AreEqual(2, slices.Length);
            Assert.AreEqual(0.5, slices[0].Fraction, 1e-9);
            Assert.AreEqual(0.5, slices[1].Fraction, 1e-9);
            Assert.AreEqual(0, slices[0].StartDeg);
            Assert.AreEqual(180, slices[0].EndDeg, 1e-6);
            Assert.AreEqual(180, slices[1].StartDeg, 1e-6);
            Assert.AreEqual(360, slices[1].EndDeg, 1e-6);
        }

        [TestMethod]
        public void Slices_QuarterUsed_FirstSliceSweeps90()
        {
            var slices = PieGeometry.Slices(0.25);
            Assert.AreEqual(2, slices.Length);
            Assert.AreEqual(0, slices[0].StartDeg);
            Assert.AreEqual(90, slices[0].EndDeg, 1e-6);
            Assert.AreEqual(90, slices[1].StartDeg, 1e-6);
            Assert.AreEqual(360, slices[1].EndDeg, 1e-6);
        }

        [TestMethod]
        public void Slices_EmptyAndFull_ReturnSingleFullCircle()
        {
            var empty = PieGeometry.Slices(0);
            Assert.AreEqual(1, empty.Length);
            Assert.AreEqual(360, empty[0].EndDeg, 1e-6);

            var full = PieGeometry.Slices(1);
            Assert.AreEqual(1, full.Length);
            Assert.AreEqual(360, full[0].EndDeg, 1e-6);
        }

        [TestMethod]
        public void Slices_OutOfRange_ClampsTo01()
        {
            var neg = PieGeometry.Slices(-0.5);
            Assert.AreEqual(1, neg.Length);

            var over = PieGeometry.Slices(1.7);
            Assert.AreEqual(1, over.Length);
        }

        [TestMethod]
        public void ArcPoint_ZeroDegrees_IsTopOfCircle()
        {
            var p = PieGeometry.ArcPoint(100, 100, 50, 0);
            Assert.AreEqual(100, p.X, 1e-6);
            Assert.AreEqual(50, p.Y, 1e-6);
        }

        [TestMethod]
        public void ArcPoint_RightAngle_IsRightSide()
        {
            var p = PieGeometry.ArcPoint(100, 100, 50, 90);
            Assert.AreEqual(150, p.X, 1e-6);
            Assert.AreEqual(100, p.Y, 1e-6);
        }

        [TestMethod]
        public void IsLargeArc_SweepOver180_True()
        {
            Assert.IsTrue(PieGeometry.IsLargeArc(0, 181));
            Assert.IsFalse(PieGeometry.IsLargeArc(0, 179));
        }
    }
}
