using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Profiles.Delay
{
    public interface IDelayProfileService
    {
        DelayProfile Add(DelayProfile profile);
        DelayProfile Update(DelayProfile profile);
        void Delete(int id);
        List<DelayProfile> All();
        DelayProfile Get(int id);
        DelayProfile GetDefaultProfile();
        List<DelayProfile> AllForTag(int tagId);
        List<DelayProfile> AllForTags(HashSet<int> tagIds);
        DelayProfile BestForTags(HashSet<int> tagIds);
        List<DelayProfile> Reorder(int id, int? afterId);
    }

    public class DelayProfileService : IDelayProfileService
    {
        private readonly IDelayProfileRepository _repo;
        private readonly ICached<DelayProfile> _bestForTagsCache;
        private readonly List<IDownloadProtocol> _downloadProtocols;

        // Add, Update, Delete and Reorder each read orders, then write them back
        private readonly object _syncRoot = new object();

        public DelayProfileService(IDelayProfileRepository repo,
                                   IEnumerable<IDownloadProtocol> downloadProtocols,
                                   ICacheManager cacheManager)
        {
            _repo = repo;
            _downloadProtocols = downloadProtocols.ToList();
            _bestForTagsCache = cacheManager.GetCache<DelayProfile>(GetType(), "best");
        }

        public DelayProfile Add(DelayProfile profile)
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

        public DelayProfile Update(DelayProfile profile)
        {
            lock (_syncRoot)
            {
                // Order only changes through Reorder
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

        public List<DelayProfile> All()
        {
            return _repo.All().Select(x => AddMissingItems(x)).ToList();
        }

        public DelayProfile Get(int id)
        {
            return AddMissingItems(_repo.Get(id));
        }

        public DelayProfile GetDefaultProfile()
        {
            var standardTypes = new[] { typeof(UsenetDownloadProtocol), typeof(TorrentDownloadProtocol) };

            var others = _downloadProtocols.Where(x => !standardTypes.Contains(x.GetType())).OrderBy(x => x.GetType().Name);

            var result = new DelayProfile();
            result.Items.AddRange(others.Select(x => GetProtocolItem(x, true)));

            return result;
        }

        public List<DelayProfile> AllForTag(int tagId)
        {
            return All().Where(r => r.Tags.Contains(tagId))
                        .ToList();
        }

        public List<DelayProfile> AllForTags(HashSet<int> tagIds)
        {
            return All().Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()).ToList();
        }

        public DelayProfile BestForTags(HashSet<int> tagIds)
        {
            var key = "-" + tagIds.Select(v => v.ToString()).Join(",");
            var cached = _bestForTagsCache.Find(key);

            // RSS sync looks this up for every release, so only a miss waits for the writers' lock
            if (cached != null)
            {
                return cached;
            }

            // Misses read and store under the writers' lock, so their cache clear can't land in between
            lock (_syncRoot)
            {
                return _bestForTagsCache.Get(key, () => FetchBestForTags(tagIds), TimeSpan.FromSeconds(30));
            }
        }

        private DelayProfile FetchBestForTags(HashSet<int> tagIds)
        {
            return All()
                .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                .OrderBy(d => d.Order).First();
        }

        public List<DelayProfile> Reorder(int id, int? afterId)
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

        private DelayProfile AddMissingItems(DelayProfile profile)
        {
            var missing = _downloadProtocols.Where(x => !profile.Items.Any(i => i.Protocol == x.GetType().Name));
            profile.Items.AddRange(missing.Select(x => GetProtocolItem(x, false)));

            var protocolNames = _downloadProtocols.Select(x => x.GetType().Name).ToList();
            profile.Items.RemoveAll(x => !protocolNames.Contains(x.Protocol));
            return profile;
        }

        private DelayProfileProtocolItem GetProtocolItem(IDownloadProtocol protocol, bool allowed)
        {
            return new DelayProfileProtocolItem
            {
                Name = protocol.GetType().Name.Replace("DownloadProtocol", ""),
                Protocol = protocol.GetType().Name,
                Allowed = allowed
            };
        }
    }
}
