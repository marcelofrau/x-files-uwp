using SQLite;

namespace XFiles.Metadata
{
    public class MetadataCacheEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed(Unique = true)]
        public string CacheKey { get; set; }

        public string Artist { get; set; }
        public string Title { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public string Year { get; set; }
        public string TrackNumber { get; set; }
        public int DurationSeconds { get; set; }
        public string MusicBrainzId { get; set; }
        public string ReleaseMbid { get; set; }
        public float Confidence { get; set; }
        public string Source { get; set; }

        [Indexed]
        public string CoverArtAlbumKey { get; set; }

        public long Timestamp { get; set; }
    }

    public class CoverArtEntry
    {
        [PrimaryKey]
        public string AlbumKey { get; set; }

        public byte[] ArtData { get; set; }
        public string Mime { get; set; }
        public string CoverUrl { get; set; }

        public long Timestamp { get; set; }
    }
}
