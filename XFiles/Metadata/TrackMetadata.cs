using System;

namespace XFiles.Metadata
{
    public class TrackMetadata
    {
        public string Title;
        public string Artist;
        public string Album;
        public string Genre;
        public string Year;
        public string TrackNumber;
        public int DurationSeconds;
        public byte[] AlbumArt;
        public string AlbumArtMime;

        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
        public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);
        public bool HasAlbum => !string.IsNullOrWhiteSpace(Album);
        public bool HasGenre => !string.IsNullOrWhiteSpace(Genre);
        public bool HasYear => !string.IsNullOrWhiteSpace(Year);
        public bool HasTrackNumber => !string.IsNullOrWhiteSpace(TrackNumber);
        public bool HasDuration => DurationSeconds > 0;
        public bool HasAlbumArt => AlbumArt != null && AlbumArt.Length > 0;

        public int CompletenessScore
        {
            get
            {
                int score = 0;
                if (HasTitle) score++;
                if (HasArtist) score++;
                if (HasAlbum) score++;
                if (HasGenre) score++;
                if (HasYear) score++;
                if (HasTrackNumber) score++;
                if (HasDuration) score++;
                if (HasAlbumArt) score++;
                return score;
            }
        }

        public static TrackMetadata FromId3Tag(FileSystem.Id3Tag tag, string filePath)
        {
            if (tag == null)
                return new TrackMetadata { Title = System.IO.Path.GetFileNameWithoutExtension(filePath) };

            return new TrackMetadata
            {
                Title = tag.Title,
                Artist = tag.Artist,
                Album = tag.Album,
                Genre = tag.Genre,
                Year = tag.Year,
                TrackNumber = tag.TrackNumber,
                DurationSeconds = tag.DurationSeconds,
                AlbumArt = tag.AlbumArt,
                AlbumArtMime = tag.AlbumArtMime
            };
        }

        public void MergeFrom(TrackMetadata source)
        {
            if (source == null) return;
            if (!HasTitle && source.HasTitle) Title = source.Title;
            if (!HasArtist && source.HasArtist) Artist = source.Artist;
            if (!HasAlbum && source.HasAlbum) Album = source.Album;
            if (!HasGenre && source.HasGenre) Genre = source.Genre;
            if (!HasYear && source.HasYear) Year = source.Year;
            if (!HasTrackNumber && source.HasTrackNumber) TrackNumber = source.TrackNumber;
            if (!HasDuration && source.HasDuration) DurationSeconds = source.DurationSeconds;
            if (!HasAlbumArt && source.HasAlbumArt)
            {
                AlbumArt = source.AlbumArt;
                AlbumArtMime = source.AlbumArtMime;
            }
        }

        public void MergeFromId3(FileSystem.Id3Tag tag)
        {
            if (tag == null) return;
            if (!HasTitle && tag.Title != null) Title = tag.Title;
            if (!HasArtist && tag.Artist != null) Artist = tag.Artist;
            if (!HasAlbum && tag.Album != null) Album = tag.Album;
            if (!HasGenre && tag.Genre != null) Genre = tag.Genre;
            if (!HasYear && tag.Year != null) Year = tag.Year;
            if (!HasTrackNumber && tag.TrackNumber != null) TrackNumber = tag.TrackNumber;
            if (!HasDuration && tag.DurationSeconds > 0) DurationSeconds = tag.DurationSeconds;
            if (!HasAlbumArt && tag.AlbumArt != null && tag.AlbumArt.Length > 0)
            {
                AlbumArt = tag.AlbumArt;
                AlbumArtMime = tag.AlbumArtMime;
            }
        }
    }
}
