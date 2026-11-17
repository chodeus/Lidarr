namespace NzbDrone.Core.Statistics
{
    public class LibraryStatistics
    {
        public int ArtistCount { get; set; }
        public int MonitoredArtistCount { get; set; }
        public int CompletedArtistCount { get; set; }
        public int ContinuingArtistCount { get; set; }
        public int EndedArtistCount { get; set; }
        public int DeletedArtistCount { get; set; }
        public int AlbumCount { get; set; }
        public int MonitoredAlbumCount { get; set; }
        public int CompletedAlbumCount { get; set; }
        public int MissingAlbumCount { get; set; }
        public int TotalTrackCount { get; set; }
        public int MonitoredTrackCount { get; set; }
        public int DownloadedTrackCount { get; set; }
        public int MissingTrackCount { get; set; }
        public int UnreleasedTrackCount { get; set; }
        public int TrackFileCount { get; set; }
        public long SizeOnDisk { get; set; }
    }
}
