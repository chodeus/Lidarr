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
        MetadataSourceProfile Get(int id);
        MetadataSourceProfile GetDefaultProfile();
        MetadataSourceProfile BestForTags(HashSet<int> tagIds);
        bool HasTaggedProfiles();
        List<MetadataSourceProfile> Reorder(int id, int? afterId);
    }

    public class MetadataSourceProfileService : IMetadataSourceProfileService, IHandle<ProviderDeletedEvent<IMetadataSource>>
    {
        public const int DefaultProfileId = 1;

        private readonly IMetadataSourceProfileRepository _repo;
        private readonly ICached<MetadataSourceProfile> _bestForTagsCache;
        private readonly ICached<bool> _taggedCache;

        public MetadataSourceProfileService(IMetadataSourceProfileRepository repo, ICacheManager cacheManager)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<MetadataSourceProfile>(GetType(), "best");
            _taggedCache = cacheManager.GetCache<bool>(GetType(), "tagged");
        }

        public MetadataSourceProfile Add(MetadataSourceProfile profile)
        {
            profile.Order = _repo.Count();

            var result = _repo.Insert(profile);
            ClearCaches();

            return result;
        }

        public MetadataSourceProfile Update(MetadataSourceProfile profile)
        {
            // the seeded default must stay the last-resort match
            if (profile.Id == DefaultProfileId)
            {
                profile.Order = int.MaxValue;
            }

            var result = _repo.Update(profile);
            ClearCaches();

            return result;
        }

        public void Delete(int id)
        {
            _repo.Delete(id);

            var all = All().OrderBy(d => d.Order).ToList();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Id == DefaultProfileId)
                {
                    continue;
                }

                all[i].Order = i + 1;
            }

            _repo.UpdateMany(all);
            ClearCaches();
        }

        public List<MetadataSourceProfile> All()
        {
            return _repo.All().ToList();
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
            return _taggedCache.Get("tagged", () => All().Any(p => p.Tags.Any()), TimeSpan.FromSeconds(30));
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
            var all = All().OrderBy(d => d.Order).ToList();

            var moving = all.SingleOrDefault(d => d.Id == id);
            var after = afterId.HasValue ? all.SingleOrDefault(d => d.Id == afterId) : null;

            if (moving == null)
            {
                return all;
            }

            var afterOrder = GetAfterOrder(moving, after);
            var afterCount = afterOrder + 2;
            var movingOrder = moving.Order;

            foreach (var profile in all)
            {
                if (profile.Id == DefaultProfileId)
                {
                    continue;
                }

                if (profile.Id == id)
                {
                    profile.Order = afterOrder + 1;
                }
                else if (profile.Id == after?.Id)
                {
                    profile.Order = afterOrder;
                }
                else if (profile.Order > afterOrder)
                {
                    profile.Order = afterCount;
                    afterCount++;
                }
                else if (profile.Order > movingOrder)
                {
                    profile.Order--;
                }
            }

            _repo.UpdateMany(all);
            ClearCaches();

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
            _taggedCache.Clear();
        }

        private static int GetAfterOrder(MetadataSourceProfile moving, MetadataSourceProfile after)
        {
            if (after == null)
            {
                return 0;
            }

            if (moving.Order < after.Order)
            {
                return after.Order - 1;
            }

            return after.Order;
        }
    }
}
