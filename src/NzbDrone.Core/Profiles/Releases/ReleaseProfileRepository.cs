using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Releases
{
    public interface IRestrictionRepository : IBasicRepository<ReleaseProfile>
    {
    }

    public class ReleaseProfileRepository : BasicRepository<ReleaseProfile>, IRestrictionRepository
    {
        public ReleaseProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        protected override List<ReleaseProfile> Query(SqlBuilder builder)
        {
            var profiles = base.Query(builder);
            profiles.ForEach(p => p.TakeIndexerSavedByAnOlderBuild());

            return profiles;
        }
    }
}
