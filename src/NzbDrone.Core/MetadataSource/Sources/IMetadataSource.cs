using System;
using System.Collections.Generic;
using NzbDrone.Core.Music;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // Throw ArtistNotFoundException or AlbumNotFoundException only for a genuine not-found; any other exception counts as the source being down.
    public interface IMetadataSource : IProvider
    {
        string IdFamily { get; }

        bool CanHandleId(string foreignId);

        // This source's artist ID for a link to its own artist page, such as one MusicBrainz lists; null for any other link
        string IdFromLink(string url);

        Artist GetArtistInfo(string foreignArtistId, int metadataProfileId);

        // Null when the source can't list changes, so every artist is refreshed
        HashSet<string> GetChangedArtists(DateTime startTime);

        Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId);

        HashSet<string> GetChangedAlbums(DateTime startTime);

        List<Artist> SearchForNewArtist(string title);

        List<Album> SearchForNewAlbum(string title, string artist);

        List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds);

        List<object> SearchForNewEntity(string title);
    }
}
