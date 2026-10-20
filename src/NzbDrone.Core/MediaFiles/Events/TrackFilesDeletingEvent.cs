using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Published with every file of a batch before they are dropped from the database. A TrackFileDeletedEvent follows for
    // each file once they are all gone.
    public class TrackFilesDeletingEvent : IEvent
    {
        public List<TrackFile> TrackFiles { get; }
        public DeleteMediaFileReason Reason { get; }

        public TrackFilesDeletingEvent(List<TrackFile> trackFiles, DeleteMediaFileReason reason)
        {
            TrackFiles = trackFiles;
            Reason = reason;
        }
    }
}
