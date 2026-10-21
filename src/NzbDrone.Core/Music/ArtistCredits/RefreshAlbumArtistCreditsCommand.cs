using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public class RefreshAlbumArtistCreditsCommand : Command
    {
        // Set for albums that need their credit now; the scheduled run leaves it empty
        public List<int> AlbumIds { get; set; }

        public override bool IsLongRunning => true;

        public override bool UpdateScheduledTask => AlbumIds?.Any() != true;
    }
}
