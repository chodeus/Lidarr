using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Housekeeping
{
    public class HousekeepingCommand : Command
    {
        // Never beside an import, whose relink of a file imported again would lose to the orphan cleanups
        public override bool RequiresDiskAccess => true;
    }
}
