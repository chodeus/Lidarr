using System;
using System.Collections.Generic;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Music;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // A source that answers through Lidarr's own lookup, against whichever server that lookup points at
    public abstract class SkyHookMetadataSourceBase<TSettings> : MetadataSourceBase<TSettings>
        where TSettings : IProviderConfig, new()
    {
        public override string IdFamily => MetadataIdFamilies.MusicBrainz;

        protected abstract SkyHookProxy SkyHook { get; }

        public override bool CanHandleId(string foreignId) => MusicBrainzIds.IsMusicBrainzId(foreignId);

        public override Artist GetArtistInfo(string foreignArtistId, int metadataProfileId) => SkyHook.GetArtistInfo(foreignArtistId, metadataProfileId);

        public override HashSet<string> GetChangedArtists(DateTime startTime) => SkyHook.GetChangedArtists(startTime);

        public override Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId) => SkyHook.GetAlbumInfo(foreignAlbumId);

        public override HashSet<string> GetChangedAlbums(DateTime startTime) => SkyHook.GetChangedAlbums(startTime);

        public override List<Artist> SearchForNewArtist(string title) => SkyHook.SearchForNewArtist(title);

        public override List<Album> SearchForNewAlbum(string title, string artist) => SkyHook.SearchForNewAlbum(title, artist);

        public override List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds) => SkyHook.SearchForNewAlbumByRecordingIds(recordingIds);

        public override List<object> SearchForNewEntity(string title) => SkyHook.SearchForNewEntity(title);
    }
}
