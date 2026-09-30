using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Tagging
{
    public interface ITaggingProfileService
    {
        TaggingProfile Add(TaggingProfile profile);
        TaggingProfile Update(TaggingProfile profile);
        void Delete(int id);
        List<TaggingProfile> All();
        TaggingProfile Get(int id);
        TaggingProfile GetDefaultProfile();
        TaggingProfile GetSeededDefaultProfile();
        List<TaggingProfile> AllForTag(int tagId);
        List<TaggingProfile> AllForTags(HashSet<int> tagIds);
        TaggingProfile BestForTags(HashSet<int> tagIds);
        List<TaggingProfile> Reorder(int id, int? afterId);
    }

    public class TaggingProfileService : ITaggingProfileService
    {
        private readonly ITaggingProfileRepository _repo;
        private readonly ICached<TaggingProfile> _bestForTagsCache;

        public TaggingProfileService(ITaggingProfileRepository repo,
                                     ICacheManager cacheManager)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<TaggingProfile>(GetType(), "best");
        }

        public TaggingProfile Add(TaggingProfile profile)
        {
            profile.Order = ProfileOrder.Next(_repo.All(), p => p.Order);

            var result = _repo.Insert(profile);
            _bestForTagsCache.Clear();

            return result;
        }

        public TaggingProfile Update(TaggingProfile profile)
        {
            // Order only changes through Reorder, and the seeded default stays the last-resort match
            profile.Order = profile.Id == ProfileOrder.DefaultProfileId ? int.MaxValue : _repo.Get(profile.Id).Order;

            var result = _repo.Update(profile);
            _bestForTagsCache.Clear();
            return result;
        }

        public void Delete(int id)
        {
            _repo.Delete(id);

            var all = All();
            ProfileOrder.Renumber(all, p => p.Order, (p, order) => p.Order = order);

            _repo.UpdateMany(all);
            _bestForTagsCache.Clear();
        }

        public List<TaggingProfile> All()
        {
            return _repo.All().ToList();
        }

        public TaggingProfile Get(int id)
        {
            return _repo.Get(id);
        }

        public TaggingProfile GetDefaultProfile()
        {
            return new TaggingProfile();
        }

        public TaggingProfile GetSeededDefaultProfile()
        {
            return _bestForTagsCache.Get("seeded-default", () => All().FirstOrDefault(p => p.Id == 1) ?? new TaggingProfile(), TimeSpan.FromSeconds(30));
        }

        public List<TaggingProfile> AllForTag(int tagId)
        {
            return All().Where(r => r.Tags.Contains(tagId))
                        .ToList();
        }

        public List<TaggingProfile> AllForTags(HashSet<int> tagIds)
        {
            return All().Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()).ToList();
        }

        public TaggingProfile BestForTags(HashSet<int> tagIds)
        {
            var key = "-" + tagIds.Select(v => v.ToString()).Join(",");
            return _bestForTagsCache.Get(key, () => FetchBestForTags(tagIds), TimeSpan.FromSeconds(30));
        }

        private TaggingProfile FetchBestForTags(HashSet<int> tagIds)
        {
            return All()
                .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                .OrderBy(d => d.Order).FirstOrDefault();
        }

        public List<TaggingProfile> Reorder(int id, int? afterId)
        {
            var all = All();

            if (ProfileOrder.Reorder(all, id, afterId, p => p.Order, (p, order) => p.Order = order))
            {
                _repo.UpdateMany(all);
                _bestForTagsCache.Clear();
            }

            return All();
        }
    }
}
