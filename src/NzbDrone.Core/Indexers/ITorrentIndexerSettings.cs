namespace NzbDrone.Core.Indexers
{
    public interface ITorrentIndexerSettings : IIndexerSettings
    {
        int MinimumSeeders { get; set; }

        SeedCriteriaSettings SeedCriteria { get; set; }
        bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        // Default keeps plugin settings compiled against develop loading; Lidarr's own indexers override it
        bool RejectTorrentFilesWithBlockedExtensionsWhileGrabbing
        {
            get => false;
            set { }
        }
    }
}
