using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public class RefreshAlbumArtistCreditsCommand : Command
    {
        public override bool IsLongRunning => true;
    }
}
