using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Profiles.Releases
{
    public interface IReleaseProfileService
    {
        List<ReleaseProfile> All();
        List<ReleaseProfile> AllForTag(int tagId);
        List<ReleaseProfile> AllExcludedForTag(int tagId) => All().Where(r => r.ExcludedTags.Contains(tagId)).ToList();
        List<ReleaseProfile> AllForTags(HashSet<int> tagIds);
        List<ReleaseProfile> EnabledForTags(HashSet<int> tagIds, int indexerId);
        ReleaseProfile Get(int id);
        void Delete(int id);
        ReleaseProfile Add(ReleaseProfile restriction);
        ReleaseProfile Update(ReleaseProfile restriction);
    }

    public class ReleaseProfileService : IReleaseProfileService, IHandle<ProviderDeletedEvent<IIndexer>>
    {
        private readonly IRestrictionRepository _repo;
        private readonly Logger _logger;

        public ReleaseProfileService(IRestrictionRepository repo, Logger logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public List<ReleaseProfile> All()
        {
            var all = _repo.All().ToList();

            return all;
        }

        public List<ReleaseProfile> AllForTag(int tagId)
        {
            return _repo.All().Where(r => r.Tags.Contains(tagId)).ToList();
        }

        public List<ReleaseProfile> AllExcludedForTag(int tagId)
        {
            return _repo.All().Where(r => r.ExcludedTags.Contains(tagId)).ToList();
        }

        public List<ReleaseProfile> AllForTags(HashSet<int> tagIds)
        {
            return _repo.All().Where(r => (r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()) && !r.ExcludedTags.Intersect(tagIds).Any()).ToList();
        }

        public List<ReleaseProfile> EnabledForTags(HashSet<int> tagIds, int indexerId)
        {
            return AllForTags(tagIds)
                .Where(r => r.Enabled)
                .Where(r => r.IndexerIds.Empty() || r.IndexerIds.Contains(indexerId)).ToList();
        }

        public ReleaseProfile Get(int id)
        {
            return _repo.Get(id);
        }

        public void Delete(int id)
        {
            _repo.Delete(id);
        }

        public ReleaseProfile Add(ReleaseProfile restriction)
        {
            return _repo.Insert(restriction);
        }

        public ReleaseProfile Update(ReleaseProfile restriction)
        {
            return _repo.Update(restriction);
        }

        // A profile limited to a deleted indexer would match nothing, and dropping its last indexer would widen it to all, so it is disabled
        public void Handle(ProviderDeletedEvent<IIndexer> message)
        {
            var changed = _repo.All().Where(p => p.IndexerIds.Remove(message.ProviderId)).ToList();

            foreach (var profile in changed.Where(p => p.IndexerIds.Empty() && p.Enabled))
            {
                profile.Enabled = false;
                _logger.Warn("Release profile {0} was limited to a deleted indexer and has been disabled", profile.Id);
            }

            _repo.UpdateMany(changed);
        }
    }
}
