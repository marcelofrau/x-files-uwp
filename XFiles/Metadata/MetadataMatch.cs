namespace XFiles.Metadata
{
    public enum MatchSource
    {
        Cache,
        MusicBrainz,
        Deezer,
        Filename,
        Id3Only
    }

    public class MetadataMatch
    {
        public TrackMetadata Metadata;
        public float Confidence;
        public MatchSource Source;
        public string MusicBrainzId;
        public string ReleaseMbid;
        public byte[] CoverArtBytes;
        public string CoverArtUrl;

        public bool IsUsable => Confidence >= 0.8f;

        public static MetadataMatch FromCache(TrackMetadata cached)
        {
            return new MetadataMatch
            {
                Metadata = cached,
                Confidence = 1.0f,
                Source = MatchSource.Cache
            };
        }

        public static MetadataMatch FromMusicBrainz(TrackMetadata meta, float confidence, string mbid)
        {
            return new MetadataMatch
            {
                Metadata = meta,
                Confidence = confidence,
                Source = MatchSource.MusicBrainz,
                MusicBrainzId = mbid
            };
        }

        public static MetadataMatch FromDeezer(TrackMetadata meta, float confidence, string coverUrl)
        {
            return new MetadataMatch
            {
                Metadata = meta,
                Confidence = confidence,
                Source = MatchSource.Deezer,
                CoverArtUrl = coverUrl
            };
        }

        public static MetadataMatch FromFilename(TrackMetadata meta)
        {
            return new MetadataMatch
            {
                Metadata = meta,
                Confidence = 0.4f,
                Source = MatchSource.Filename
            };
        }

        public static MetadataMatch FromId3Only(TrackMetadata meta)
        {
            return new MetadataMatch
            {
                Metadata = meta,
                Confidence = 0.0f,
                Source = MatchSource.Id3Only
            };
        }
    }
}
