using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Metadata;

namespace XFiles.Tests
{
    [TestClass]
    public class FilenameParserTests
    {
        [TestMethod]
        public void ExtractFromPath_TrackAndArtistAlbumFromParent_AllFields()
        {
            var meta = FilenameParser.ExtractFromPath(@"C:\Music\Artist - Album\01 - Intro.mp3");

            Assert.AreEqual("01", meta.TrackNumber);
            Assert.AreEqual("Intro", meta.Title);
            Assert.AreEqual("Artist", meta.Artist);
            Assert.AreEqual("Album", meta.Album);
        }

        [TestMethod]
        public void ExtractFromPath_NoTrackPrefix_ArtistAlbumFromParent()
        {
            var meta = FilenameParser.ExtractFromPath(@"D:\Songs\Pink Floyd - The Wall\Another Brick.mp3");

            Assert.AreEqual("Pink Floyd", meta.Artist);
            Assert.AreEqual("The Wall", meta.Album);
            Assert.AreEqual("Another Brick", meta.Title);
            Assert.IsFalse(meta.HasTrackNumber);
        }

        [TestMethod]
        public void ExtractFromPath_TrackUnderscoreAndYear_Stripped()
        {
            var meta = FilenameParser.ExtractFromPath(@"x\05_Song_Name_(2021).mp3");

            Assert.AreEqual("05", meta.TrackNumber);
            Assert.AreEqual("Song Name", meta.Title);
        }

        [TestMethod]
        public void ExtractFromPath_ArtistAlbumFromFilename()
        {
            var meta = FilenameParser.ExtractFromPath(@"x\Artist - Title [remaster].mp3");

            Assert.AreEqual("Artist", meta.Artist);
            // Without a track prefix, the whole cleaned filename stays as Title.
            Assert.AreEqual("Artist - Title", meta.Title);
        }

        [TestMethod]
        public void ExtractFromPath_EmptyString_ReturnsEmptyMetadata()
        {
            var meta = FilenameParser.ExtractFromPath("");

            Assert.IsFalse(meta.HasTitle);
            Assert.IsFalse(meta.HasArtist);
            Assert.IsFalse(meta.HasAlbum);
            Assert.IsFalse(meta.HasTrackNumber);
        }

        [TestMethod]
        public void ExtractFromPath_Null_ReturnsEmptyMetadata()
        {
            var meta = FilenameParser.ExtractFromPath(null);

            Assert.IsFalse(meta.HasTitle);
        }

        [TestMethod]
        public void ExtractFromPath_NoSeparators_TitleOnly()
        {
            var meta = FilenameParser.ExtractFromPath(@"standalone.mp3");

            Assert.AreEqual("standalone", meta.Title);
            Assert.IsFalse(meta.HasArtist);
            Assert.IsFalse(meta.HasAlbum);
        }
    }
}
