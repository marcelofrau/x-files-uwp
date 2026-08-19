using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Metadata;

namespace XFiles.Tests
{
    [TestClass]
    public class MetadataMatchTests
    {
        // ── IsUsable ──────────────────────────────────────────────

        [TestMethod]
        public void IsUsable_HighConfidence_ReturnsTrue()
        {
            var match = new MetadataMatch { Confidence = 0.9f };
            Assert.IsTrue(match.IsUsable);
        }

        [TestMethod]
        public void IsUsable_ExactThreshold_ReturnsTrue()
        {
            var match = new MetadataMatch { Confidence = 0.8f };
            Assert.IsTrue(match.IsUsable);
        }

        [TestMethod]
        public void IsUsable_BelowThreshold_ReturnsFalse()
        {
            var match = new MetadataMatch { Confidence = 0.79f };
            Assert.IsFalse(match.IsUsable);
        }

        [TestMethod]
        public void IsUsable_ZeroConfidence_ReturnsFalse()
        {
            var match = new MetadataMatch { Confidence = 0.0f };
            Assert.IsFalse(match.IsUsable);
        }

        // ── Factory methods ───────────────────────────────────────

        [TestMethod]
        public void FromCache_CreatesUsableMatch()
        {
            var meta = new TrackMetadata { Title = "Cached" };
            var match = MetadataMatch.FromCache(meta);
            Assert.AreEqual(1.0f, match.Confidence);
            Assert.AreEqual(MatchSource.Cache, match.Source);
            Assert.IsTrue(match.IsUsable);
            Assert.AreEqual("Cached", match.Metadata.Title);
        }

        [TestMethod]
        public void FromId3Only_CreatesUnusableMatch()
        {
            var meta = new TrackMetadata { Title = "ID3 Only" };
            var match = MetadataMatch.FromId3Only(meta);
            Assert.AreEqual(0.0f, match.Confidence);
            Assert.AreEqual(MatchSource.Id3Only, match.Source);
            Assert.IsFalse(match.IsUsable);
        }

        [TestMethod]
        public void FromFilename_CreatesLowConfidenceMatch()
        {
            var meta = new TrackMetadata { Title = "Parsed" };
            var match = MetadataMatch.FromFilename(meta);
            Assert.AreEqual(0.4f, match.Confidence);
            Assert.AreEqual(MatchSource.Filename, match.Source);
            Assert.IsFalse(match.IsUsable);
        }

        [TestMethod]
        public void FromDeezer_CreatesMatchWithCoverUrl()
        {
            var meta = new TrackMetadata { Title = "Online" };
            var match = MetadataMatch.FromDeezer(meta, 0.95f, "https://example.com/cover.jpg");
            Assert.AreEqual(0.95f, match.Confidence);
            Assert.AreEqual(MatchSource.Deezer, match.Source);
            Assert.AreEqual("https://example.com/cover.jpg", match.CoverArtUrl);
            Assert.IsTrue(match.IsUsable);
        }

        [TestMethod]
        public void FromMusicBrainz_CreatesMatchWithMbid()
        {
            var meta = new TrackMetadata { Title = "MB Match" };
            var match = MetadataMatch.FromMusicBrainz(meta, 0.85f, "abc-123-mbid");
            Assert.AreEqual(0.85f, match.Confidence);
            Assert.AreEqual(MatchSource.MusicBrainz, match.Source);
            Assert.AreEqual("abc-123-mbid", match.MusicBrainzId);
            Assert.IsTrue(match.IsUsable);
        }

        [TestMethod]
        public void FromMusicBrainz_WithReleaseMbid()
        {
            var meta = new TrackMetadata();
            var match = MetadataMatch.FromMusicBrainz(meta, 0.9f, "rec-mbid");
            match.ReleaseMbid = "rel-mbid";
            Assert.AreEqual("rel-mbid", match.ReleaseMbid);
        }
    }
}
