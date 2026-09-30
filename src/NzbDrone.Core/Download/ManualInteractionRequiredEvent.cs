using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class ManualInteractionRequiredEvent : IEvent
    {
        public RemoteAlbum Album { get; private set; }
        public TrackedDownload TrackedDownload { get; private set; }

        public ManualInteractionRequiredEvent(TrackedDownload trackedDownload)
        {
            TrackedDownload = trackedDownload;
            Album = trackedDownload.RemoteAlbum;
        }
    }
}
