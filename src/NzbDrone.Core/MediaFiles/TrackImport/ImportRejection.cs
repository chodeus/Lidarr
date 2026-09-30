using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.MediaFiles.TrackImport
{
    public class ImportRejection : Rejection
    {
        public ImportRejectionReason RejectionReason { get; }

        public ImportRejection(ImportRejectionReason reason, string message, RejectionType type = RejectionType.Permanent)
            : base(message, type)
        {
            RejectionReason = reason;
        }
    }
}
