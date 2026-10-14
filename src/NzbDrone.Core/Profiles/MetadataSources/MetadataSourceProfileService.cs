using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Profiles.MetadataSources
{
    public interface IMetadataSourceProfileService
    {
        MetadataSourceProfile Add(MetadataSourceProfile profile);
        MetadataSourceProfile Update(MetadataSourceProfile profile);
        void Delete(int id);
        List<MetadataSourceProfile> All();
        List<MetadataSourceProfile> AllForTag(int tagId);
        MetadataSourceProfile Get(int id);
        MetadataSourceProfile GetDefaultProfile();
        MetadataSourceProfile BestForTags(HashSet<int> tagIds);
        bool HasTaggedProfiles();
        bool HasSupplementarySources();
        List<MetadataSourceProfile> Reorder(int id, int? afterId);
    }

    public class MetadataSourceProfileService : IMetadataSourceProfileService, IHandle<ProviderDeletedEvent<IMetadataSource>>
    {
        public const int DefaultProfileId = ProfileOrder.DefaultProfileId;

        private readonly IMetadataSourceProfileRepository _repo;
        private readonly ICached<MetadataSourceProfile> _bestForTagsCache;
        private readonly ICached<bool> _flagCache;

        public MetadataSourceProfileService(IMetadataSourceProfileRepository repo, ICacheManager cacheManager)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<MetadataSourceProfile>(GetType(), "best");
            _flagCache = cacheManager.GetCache<bool>(GetType(), "flags");
        }

        public MetadataSourceProfile Add(MetadataSourceProfile profile)
        {
            profile.Order = ProfileOrder.Next(_repo.All(), p => p.Order);

            var result = _repo.Insert(profile);
            ClearCaches();

            return result;
        }

        public MetadataSourceProfile Update(MetadataSourceProfile profile)
        {
            // Order changes only through Reorder and Delete; the seeded default stays the last-resort match
            profile.Order = profile.Id == DefaultProfileId ? int.MaxValue : _repo.Get(profile.Id).Order;

            var result = _repo.Update(profile);
            ClearCaches();

            return result;
        }

        public void Delete(int id)
        {
            _repo.Delete(id);

            var all = All();
            ProfileOrder.Renumber(all, p => p.Order, (p, order) => p.Order = order);

            _repo.UpdateMany(all);
            ClearCaches();
        }

        public List<MetadataSourceProfile> All()
        {
            return _repo.All().ToList();
        }

        public List<MetadataSourceProfile> AllForTag(int tagId)
        {
            return _repo.All().Where(p => p.Tags.Contains(tagId)).ToList();
        }

        public MetadataSourceProfile Get(int id)
        {
            return _repo.Get(id);
        }

        public MetadataSourceProfile GetDefaultProfile()
        {
            return _bestForTagsCache.Get("default", () => All().FirstOrDefault(p => p.Id == DefaultProfileId) ?? new MetadataSourceProfile(), TimeSpan.FromSeconds(30));
        }

        public MetadataSourceProfile BestForTags(HashSet<int> tagIds)
        {
            var key = "-" + tagIds.Select(v => v.ToString()).Join(",");
            return _bestForTagsCache.Get(key, () => FetchBestForTags(tagIds), TimeSpan.FromSeconds(30));
        }

        public bool HasTaggedProfiles()
        {
            return _flagCache.Get("tagged", () => All().Any(p => p.Tags.Any()), TimeSpan.FromSeconds(30));
        }

        public bool HasSupplementarySources()
        {
            return _flagCache.Get("supplementary", () => All().Any(p => p.Items.Any(i => i.Role == MetadataSourceRole.Supplementary)), TimeSpan.FromSeconds(30));
        }

        private MetadataSourceProfile FetchBestForTags(HashSet<int> tagIds)
        {
            return All()
                .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                .OrderBy(d => d.Order)
                .FirstOrDefault() ?? GetDefaultProfile();
        }

        public List<MetadataSourceProfile> Reorder(int id, int? afterId)
        {
            var all = All();

            if (ProfileOrder.Reorder(all, id, afterId, p => p.Order, (p, order) => p.Order = order))
            {
                _repo.UpdateMany(all);
                ClearCaches();
            }

            return All();
        }

        public void Handle(ProviderDeletedEvent<IMetadataSource> message)
        {
            var affected = All().Where(p => p.Items.Any(i => i.MetadataSourceId == message.ProviderId)).ToList();

            if (affected.Empty())
            {
                return;
            }

            affected.ForEach(p => p.Items.RemoveAll(i => i.MetadataSourceId == message.ProviderId));
            _repo.SetFields(affected, p => p.Items);
            ClearCaches();
        }

        private void ClearCaches()
        {
            _bestForTagsCache.Clear();
            _flagCache.Clear();
        }
    }
}
