using NzbDrone.Core.Annotations;

namespace NzbDrone.Core.Notifications
{
    public enum MetadataLinkType
    {
        [FieldOption(Label = "MusicBrainz Artist")]
        MusicBrainzArtist = 0,

        [FieldOption(Label = "MusicBrainz Album")]
        MusicBrainzAlbum = 1
    }
}
