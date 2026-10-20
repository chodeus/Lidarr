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

        // Add, Update, Delete and Reorder each read orders, then write them back
        private readonly object _syncRoot = new object();

        public TaggingProfileService(ITaggingProfileRepository repo,
                                     ICacheManager cacheManager)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<TaggingProfile>(GetType(), "best");
        }

        public TaggingProfile Add(TaggingProfile profile)
        {
            lock (_syncRoot)
            {
                try
                {
                    var all = All();

                    if (ProfileOrder.MakeRoom(all, p => p.Order, (p, order) => p.Order = order))
                    {
                        _repo.SetFields(all, p => p.Order);
                    }

                    profile.Order = ProfileOrder.Next(all, p => p.Order);

                    return _repo.Insert(profile);
                }
                finally
                {
                    // The renumber commits even when the insert fails
                    _bestForTagsCache.Clear();
                }
            }
        }

        public TaggingProfile Update(TaggingProfile profile)
        {
            lock (_syncRoot)
            {
                // Order only changes through Reorder, and the seeded default stays the last-resort match
                profile.Order = profile.Id == ProfileOrder.DefaultProfileId ? int.MaxValue : _repo.Get(profile.Id).Order;

                var result = _repo.Update(profile);
                _bestForTagsCache.Clear();
                return result;
            }
        }

        public void Delete(int id)
        {
            lock (_syncRoot)
            {
                try
                {
                    _repo.Delete(id);

                    var all = All();
                    ProfileOrder.Renumber(all, p => p.Order, (p, order) => p.Order = order);

                    // Only the order column, so a save of another profile isn't reverted
                    _repo.SetFields(all, p => p.Order);
                }
                finally
                {
                    // The delete has committed even when the renumber fails
                    _bestForTagsCache.Clear();
                }
            }
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
            return Cached("seeded-default", () => All().FirstOrDefault(p => p.Id == 1) ?? new TaggingProfile());
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
            return Cached(key, () => FetchBestForTags(tagIds));
        }

        // Tagging looks these up for every file, so only a miss waits for the writers' lock, under which it reads and
        // stores so their cache clear can't land in between
        private TaggingProfile Cached(string key, Func<TaggingProfile> fetch)
        {
            var cached = _bestForTagsCache.Find(key);

            if (cached != null)
            {
                return cached;
            }

            lock (_syncRoot)
            {
                return _bestForTagsCache.Get(key, fetch, TimeSpan.FromSeconds(30));
            }
        }

        private TaggingProfile FetchBestForTags(HashSet<int> tagIds)
        {
            return All()
                .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                .OrderBy(d => d.Order).FirstOrDefault();
        }

        public List<TaggingProfile> Reorder(int id, int? afterId)
        {
            lock (_syncRoot)
            {
                var all = All();

                if (ProfileOrder.Reorder(all, id, afterId, p => p.Order, (p, order) => p.Order = order))
                {
                    _repo.SetFields(all, p => p.Order);
                    _bestForTagsCache.Clear();
                }

                return All();
            }
        }
    }
}
