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

    public class SchemaVersionEntry
    {
        [PrimaryKey]
        public int Id { get; set; }

        public int Version { get; set; }
    }

    public class AppSettingEntry
    {
        [PrimaryKey]
        public string Key { get; set; }

        public string Value { get; set; }
    }

    public class LibRetroThumbnailEntry
    {
        [PrimaryKey]
        public string Url { get; set; }

        public bool Found { get; set; }

        public long Timestamp { get; set; }
    }

    public class NetworkServerEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>Cast of <see cref="XFiles.Network.NetworkProtocol"/>.</summary>
        public int Protocol { get; set; }

        public string DisplayName { get; set; }

        public string Host { get; set; }

        /// <summary>0 = protocol default (SMB = 445).</summary>
        public int Port { get; set; }

        public string Username { get; set; }

        public string Share { get; set; }

        /// <summary>smb://[user@]host[/share] — identity + PasswordVault resource.</summary>
        [Indexed(Unique = true)]
        public string CanonicalUrl { get; set; }
    }
}
