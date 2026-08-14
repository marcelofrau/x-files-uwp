using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class MusicFormatClassifierTests
    {
        [TestMethod]
        public void IsStandardAudio_CommonFormats_True()
        {
            foreach (string ext in new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aac", ".MP3", ".Flac" })
                Assert.IsTrue(MusicFormatClassifier.IsStandardAudio(ext), ext);
        }

        [TestMethod]
        public void IsStandardAudio_NonAudio_False()
        {
            Assert.IsFalse(MusicFormatClassifier.IsStandardAudio(".png"));
            Assert.IsFalse(MusicFormatClassifier.IsStandardAudio(".txt"));
            Assert.IsFalse(MusicFormatClassifier.IsStandardAudio(""));
            Assert.IsFalse(MusicFormatClassifier.IsStandardAudio(null));
        }

        [TestMethod]
        public void IsChiptune_GmeFormats_True()
        {
            foreach (string ext in new[] { ".spc", ".gbs", ".nsf", ".nsfe", ".vgm", ".vgz", ".gym", ".sid", ".ay", ".sap" })
                Assert.IsTrue(MusicFormatClassifier.IsChiptune(ext), ext);
        }

        [TestMethod]
        public void IsChiptune_PsfUsfOpenmpt_True()
        {
            foreach (string ext in new[] { ".psf", ".minipsf", ".usf", ".miniusf", ".mod", ".xm", ".s3m", ".it" })
                Assert.IsTrue(MusicFormatClassifier.IsChiptune(ext), ext);
        }

        [TestMethod]
        public void IsMusicFile_EitherKind_True()
        {
            Assert.IsTrue(MusicFormatClassifier.IsMusicFile(".mp3"));
            Assert.IsTrue(MusicFormatClassifier.IsMusicFile(".psf"));
            Assert.IsFalse(MusicFormatClassifier.IsMusicFile(".zip"));
        }

        [TestMethod]
        public void MusicExtensions_AllClassified()
        {
            Assert.IsTrue(MusicFormatClassifier.MusicExtensions.Length >= 40);
            foreach (string ext in MusicFormatClassifier.MusicExtensions)
            {
                Assert.IsTrue(MusicFormatClassifier.IsMusicFile(ext), ext);
                Assert.IsTrue(ext.StartsWith("."), ext);
            }
        }

        [TestMethod]
        public void PercentToGain_ClampsAndMaps()
        {
            Assert.AreEqual(0.1f, MusicFormatClassifier.PercentToGain(10));
            Assert.AreEqual(1f, MusicFormatClassifier.PercentToGain(100));
            Assert.AreEqual(0f, MusicFormatClassifier.PercentToGain(-5));
            Assert.AreEqual(1f, MusicFormatClassifier.PercentToGain(200));
        }

        [TestMethod]
        public void NextVolumeLevel_Cycles()
        {
            Assert.AreEqual(25, MusicFormatClassifier.NextVolumeLevel(10));
            Assert.AreEqual(50, MusicFormatClassifier.NextVolumeLevel(25));
            Assert.AreEqual(100, MusicFormatClassifier.NextVolumeLevel(75));
            Assert.AreEqual(10, MusicFormatClassifier.NextVolumeLevel(100));
            Assert.AreEqual(10, MusicFormatClassifier.NextVolumeLevel(37)); // unknown → first
        }
    }
}
