using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Metadata;

namespace XFiles.Tests
{
    [TestClass]
    public class TrackMetadataTests
    {
        // ── FromId3Tag ────────────────────────────────────────────

        [TestMethod]
        public void FromId3Tag_NullTag_ReturnsFilenameAsTitle()
        {
            var meta = TrackMetadata.FromId3Tag(null, "/music/track.mp3");
            Assert.AreEqual("track", meta.Title);
            Assert.IsFalse(meta.HasArtist);
            Assert.IsFalse(meta.HasAlbum);
        }

        [TestMethod]
        public void FromId3Tag_PopulatedTag_MapsFields()
        {
            var tag = new FileSystem.Id3Tag
            {
                Title = "My Song",
                Artist = "Artist",
                Album = "Album",
                Genre = "Rock",
                Year = "2024",
                TrackNumber = "3",
                DurationSeconds = 240,
                AlbumArt = new byte[] { 0xFF, 0xD8 },
                AlbumArtMime = "image/jpeg"
            };
            var meta = TrackMetadata.FromId3Tag(tag, "/any/path.mp3");
            Assert.AreEqual("My Song", meta.Title);
            Assert.AreEqual("Artist", meta.Artist);
            Assert.AreEqual("Album", meta.Album);
            Assert.AreEqual("Rock", meta.Genre);
            Assert.AreEqual("2024", meta.Year);
            Assert.AreEqual("3", meta.TrackNumber);
            Assert.AreEqual(240, meta.DurationSeconds);
            Assert.IsTrue(meta.HasAlbumArt);
            Assert.AreEqual("image/jpeg", meta.AlbumArtMime);
        }

        // ── CompletenessScore ─────────────────────────────────────

        [TestMethod]
        public void CompletenessScore_EmptyMeta_ReturnsZero()
        {
            var meta = new TrackMetadata();
            Assert.AreEqual(0, meta.CompletenessScore);
        }

        [TestMethod]
        public void CompletenessScore_FullMeta_ReturnsEight()
        {
            var meta = new TrackMetadata
            {
                Title = "Song", Artist = "Artist", Album = "Album",
                Genre = "Rock", Year = "2024", TrackNumber = "1",
                DurationSeconds = 180, AlbumArt = new byte[] { 0xFF }
            };
            Assert.AreEqual(8, meta.CompletenessScore);
        }

        [TestMethod]
        public void CompletenessScore_PartialMeta_ReturnsCorrectCount()
        {
            var meta = new TrackMetadata { Title = "Song", Artist = "Artist" };
            Assert.AreEqual(2, meta.CompletenessScore);
        }

        // ── MergeFrom ─────────────────────────────────────────────

        [TestMethod]
        public void MergeFrom_FillsEmptyFields()
        {
            var target = new TrackMetadata { Title = "Existing" };
            var source = new TrackMetadata
            {
                Title = "Ignored", Artist = "New Artist", Album = "New Album",
                Genre = "Jazz", Year = "2020", TrackNumber = "5",
                DurationSeconds = 300, AlbumArt = new byte[] { 0x01 }
            };

            target.MergeFrom(source);

            Assert.AreEqual("Existing", target.Title);   // existing preserved
            Assert.AreEqual("New Artist", target.Artist); // empty → filled
            Assert.AreEqual("New Album", target.Album);
            Assert.AreEqual("Jazz", target.Genre);
            Assert.AreEqual("2020", target.Year);
            Assert.AreEqual("5", target.TrackNumber);
            Assert.AreEqual(300, target.DurationSeconds);
            Assert.IsTrue(target.HasAlbumArt);
        }

        [TestMethod]
        public void MergeFrom_NullSource_NoOp()
        {
            var target = new TrackMetadata { Title = "Song" };
            target.MergeFrom(null);
            Assert.AreEqual("Song", target.Title);
            Assert.IsFalse(target.HasArtist);
        }

        [TestMethod]
        public void MergeFrom_WhitespaceStrings_NotMerged()
        {
            var target = new TrackMetadata();
            var source = new TrackMetadata { Title = "  ", Artist = "\t" };
            target.MergeFrom(source);
            // Whitespace-only strings have HasTitle=false, so they are not merged
            Assert.IsFalse(target.HasTitle);
            Assert.IsFalse(target.HasArtist);
        }

        // ── MergeFromId3 ──────────────────────────────────────────

        [TestMethod]
        public void MergeFromId3_NullTag_NoOp()
        {
            var meta = new TrackMetadata { Title = "Song" };
            meta.MergeFromId3(null);
            Assert.AreEqual("Song", meta.Title);
        }

        [TestMethod]
        public void MergeFromId3_FillsEmptyFields()
        {
            var target = new TrackMetadata();
            var tag = new FileSystem.Id3Tag { Title = "ID3 Song", Artist = "ID3 Artist" };
            target.MergeFromId3(tag);
            Assert.AreEqual("ID3 Song", target.Title);
            Assert.AreEqual("ID3 Artist", target.Artist);
        }

        // ── Has* properties ───────────────────────────────────────

        [TestMethod]
        public void HasProperties_EmptyMeta_AllFalse()
        {
            var meta = new TrackMetadata();
            Assert.IsFalse(meta.HasTitle);
            Assert.IsFalse(meta.HasArtist);
            Assert.IsFalse(meta.HasAlbum);
            Assert.IsFalse(meta.HasGenre);
            Assert.IsFalse(meta.HasYear);
            Assert.IsFalse(meta.HasTrackNumber);
            Assert.IsFalse(meta.HasDuration);
            Assert.IsFalse(meta.HasAlbumArt);
        }

        [TestMethod]
        public void HasAlbumArt_EmptyArray_False()
        {
            var meta = new TrackMetadata { AlbumArt = new byte[0] };
            Assert.IsFalse(meta.HasAlbumArt);
        }

        [TestMethod]
        public void HasDuration_Zero_False()
        {
            var meta = new TrackMetadata { DurationSeconds = 0 };
            Assert.IsFalse(meta.HasDuration);
        }
    }
}
