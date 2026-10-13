using System;
using System.Collections.Generic;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Music;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.MetadataSource.Sources.Lidarr
{
    public class LidarrMetadataSourceSettings : IProviderConfig
    {
        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }

    // Lidarr's own metadata server, through the lookup Lidarr already uses
    public class LidarrMetadataSource : MetadataSourceBase<LidarrMetadataSourceSettings>
    {
        private readonly SkyHookProxy _skyHook;

        public LidarrMetadataSource(SkyHookProxy skyHook)
        {
            _skyHook = skyHook;
        }

        public override string Name => "Lidarr";

        public override string IdFamily => MetadataIdFamilies.MusicBrainz;

        public override bool CanHandleId(string foreignId) => MusicBrainzIds.IsMusicBrainzId(foreignId);

        public override Artist GetArtistInfo(string foreignArtistId, int metadataProfileId) => _skyHook.GetArtistInfo(foreignArtistId, metadataProfileId);

        public override HashSet<string> GetChangedArtists(DateTime startTime) => _skyHook.GetChangedArtists(startTime);

        public override Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId) => _skyHook.GetAlbumInfo(foreignAlbumId);

        public override HashSet<string> GetChangedAlbums(DateTime startTime) => _skyHook.GetChangedAlbums(startTime);

        public override List<Artist> SearchForNewArtist(string title) => _skyHook.SearchForNewArtist(title);

        public override List<Album> SearchForNewAlbum(string title, string artist) => _skyHook.SearchForNewAlbum(title, artist);

        public override List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds) => _skyHook.SearchForNewAlbumByRecordingIds(recordingIds);

        public override List<object> SearchForNewEntity(string title) => _skyHook.SearchForNewEntity(title);
    }
}
