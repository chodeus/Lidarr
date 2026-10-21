using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.Sources.Lidarr;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.MetadataSources;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public interface IMetadataSourceLabels
    {
        string LabelFor(object result);
    }

    // Answers Lidarr's metadata lookups from the sources in the artist's metadata source profile
    public class MetadataSourceAggregator : IProvideArtistInfo,
                                            IProvideAlbumInfo,
                                            ISearchForNewArtist,
                                            ISearchForNewAlbum,
                                            ISearchForNewEntity,
                                            IMetadataSourceLabels,
                                            IHandle<ProviderAddedEvent<IMetadataSource>>,
                                            IHandle<ProviderUpdatedEvent<IMetadataSource>>,
                                            IHandle<ProviderDeletedEvent<IMetadataSource>>
    {
        private readonly IMetadataSourceFactory _sourceFactory;
        private readonly IMetadataSourceProfileService _profileService;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IMetadataSource _lastResort;

        // Shared by name, so the instance that handles provider events clears the one that answers lookups
        private readonly ICached<Dictionary<int, IMetadataSource>> _sourceCache;
        private readonly ConditionalWeakTable<object, string> _labels = new();
        private readonly Logger _logger;

        public MetadataSourceAggregator(IMetadataSourceFactory sourceFactory,
                                        IMetadataSourceProfileService profileService,
                                        IArtistService artistService,
                                        IAlbumService albumService,
                                        LidarrMetadataSource lidarr,
                                        ICacheManager cacheManager,
                                        Logger logger)
        {
            _sourceFactory = sourceFactory;
            _profileService = profileService;
            _artistService = artistService;
            _albumService = albumService;
            lidarr.Definition ??= new MetadataSourceDefinition { Name = "Lidarr" };
            _lastResort = lidarr;
            _sourceCache = cacheManager.GetCache<Dictionary<int, IMetadataSource>>(GetType(), "sources");
            _logger = logger;
        }

        public Artist GetArtistInfo(string lidarrId, int metadataProfileId)
        {
            var route = RouteFor(ProfileForArtist(lidarrId), lidarrId);

            return Lookup(route, s => s.GetArtistInfo(lidarrId, metadataProfileId), () => new ArtistNotFoundException(lidarrId), MergeAlbums);
        }

        public HashSet<string> GetChangedArtists(DateTime startTime)
        {
            return Changed(s => s.GetChangedArtists(startTime));
        }

        public Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string id)
        {
            var route = RouteFor(ProfileForAlbum(id), id);

            return Lookup(route, s => s.GetAlbumInfo(id), () => new AlbumNotFoundException(id), null, HasReleases);
        }

        public HashSet<string> GetChangedAlbums(DateTime startTime)
        {
            return Changed(s => s.GetChangedAlbums(startTime));
        }

        public List<Artist> SearchForNewArtist(string title)
        {
            return Search(SearchRoute(title), s => s.SearchForNewArtist(title), a => a.ForeignArtistId);
        }

        public List<Album> SearchForNewAlbum(string title, string artist)
        {
            return Search(SearchRoute(title), s => s.SearchForNewAlbum(title, artist), a => a.ForeignAlbumId);
        }

        public List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds)
        {
            // Recording IDs are MusicBrainz IDs, so only that family can answer
            return Search(FamilyRoute(_profileService.GetDefaultProfile(), MetadataIdFamilies.MusicBrainz), s => s.SearchForNewAlbumByRecordingIds(recordingIds), a => a.ForeignAlbumId);
        }

        public List<object> SearchForNewEntity(string title)
        {
            return Search(SearchRoute(title), s => s.SearchForNewEntity(title), EntityKey);
        }

        public string LabelFor(object result)
        {
            return result != null && _labels.TryGetValue(result, out var label) ? label : null;
        }

        public void Handle(ProviderAddedEvent<IMetadataSource> message)
        {
            _sourceCache.Clear();
        }

        public void Handle(ProviderUpdatedEvent<IMetadataSource> message)
        {
            _sourceCache.Clear();
        }

        public void Handle(ProviderDeletedEvent<IMetadataSource> message)
        {
            _sourceCache.Clear();
        }

        private T Lookup<T>(Route route, Func<IMetadataSource, T> call, Func<Exception> notFound, Func<T, T, T> merge, Func<T, bool> usable = null)
        {
            Exception outage = null;
            var found = new List<T>();
            var unusable = new List<T>();

            bool Answered(IMetadataSource source, out T result)
            {
                if (!TryAsk(source, call, ref outage, out result))
                {
                    return false;
                }

                if (usable == null || usable(result))
                {
                    return true;
                }

                _logger.Debug("{0} has nothing usable for it, asking the next source", source.Definition?.Name);
                unusable.Add(result);

                return false;
            }

            foreach (var source in route.Primary)
            {
                if (!Answered(source, out var result))
                {
                    continue;
                }

                if (!route.CombineAll || merge == null)
                {
                    return result;
                }

                found.Add(result);
            }

            // Refresh treats the album list as complete, so a merge missing a down primary would drop its albums, and so would a fallback's list
            if (route.CombineAll && merge != null && outage != null)
            {
                ExceptionDispatchInfo.Capture(outage).Throw();
            }

            if (found.Any())
            {
                return found.Aggregate(merge);
            }

            foreach (var source in route.Fallback)
            {
                if (Answered(source, out var result))
                {
                    return result;
                }
            }

            // Not found only when every source said so; a source that was down might still have it
            if (outage != null)
            {
                ExceptionDispatchInfo.Capture(outage).Throw();
            }

            if (unusable.Any())
            {
                return unusable.First();
            }

            throw notFound();
        }

        // An album without releases is no answer while another source might have them
        private static bool HasReleases(Tuple<string, Album, List<ArtistMetadata>> info)
        {
            return info.Item2.AlbumReleases.Value.Any();
        }

        private List<T> Search<T>(Route route, Func<IMetadataSource, List<T>> call, Func<T, string> key)
        {
            Exception outage = null;
            var results = new List<T>();
            var seen = new HashSet<string>();

            foreach (var source in route.Primary)
            {
                if (TryAsk(source, call, ref outage, out var found) && AddUnique(results, seen, found, key, source) && !route.CombineAll)
                {
                    break;
                }
            }

            if (results.Empty())
            {
                foreach (var source in route.Fallback)
                {
                    if (TryAsk(source, call, ref outage, out var found) && AddUnique(results, seen, found, key, source))
                    {
                        break;
                    }
                }
            }

            if (results.Empty() && outage != null)
            {
                ExceptionDispatchInfo.Capture(outage).Throw();
            }

            return results;
        }

        private bool AddUnique<T>(List<T> results, HashSet<string> seen, List<T> found, Func<T, string> key, IMetadataSource source)
        {
            if (found == null || found.Empty())
            {
                return false;
            }

            foreach (var item in found)
            {
                var itemKey = key(item);

                if (itemKey == null || seen.Add(itemKey))
                {
                    results.Add(item);
                    _labels.AddOrUpdate(item, source.Definition?.Name ?? source.Name);
                }
            }

            return true;
        }

        private bool TryAsk<T>(IMetadataSource source, Func<IMetadataSource, T> call, ref Exception outage, out T result)
        {
            try
            {
                result = call(source);
                return true;
            }
            catch (Exception ex) when (ex is ArtistNotFoundException || ex is AlbumNotFoundException)
            {
                _logger.Debug("{0} does not have it: {1}", source.Definition?.Name, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn("Metadata source {0} failed: {1}", source.Definition?.Name, MetadataSourceErrors.Describe(ex));
                outage ??= ex;
            }

            result = default;
            return false;
        }

        private HashSet<string> Changed(Func<IMetadataSource, HashSet<string>> call)
        {
            var sources = EnabledSources().Values.ToList();

            // MusicBrainz lookups fall back to Lidarr's server when no enabled source takes them, so its changes count too
            if (!sources.Any(s => s is LidarrMetadataSource))
            {
                sources.Add(_lastResort);
            }

            var changed = new HashSet<string>();

            foreach (var source in sources)
            {
                HashSet<string> ids;

                try
                {
                    ids = call(source);
                }
                catch (Exception ex)
                {
                    _logger.Info("{0} could not list changes: {1}", source.Definition?.Name, MetadataSourceErrors.Describe(ex));
                    return null;
                }

                // A source that can't list changes means everything is refreshed
                if (ids == null)
                {
                    return null;
                }

                changed.UnionWith(ids);
            }

            return changed;
        }

        private static Artist MergeAlbums(Artist first, Artist other)
        {
            var albums = first.Albums.Value;
            var known = new HashSet<string>(albums.SelectMany(a => (a.OldForeignAlbumIds ?? new List<string>()).Append(a.ForeignAlbumId)));

            // A source that hasn't caught up with a merge may still list the album by an ID the other one keeps as old
            albums.AddRange(other.Albums.Value.Where(a => !known.Contains(a.ForeignAlbumId) && !(a.OldForeignAlbumIds ?? new List<string>()).Any(known.Contains)));

            return first;
        }

        private static string EntityKey(object entity)
        {
            return entity switch
            {
                Artist artist => "artist:" + artist.ForeignArtistId,
                Album album => "album:" + album.ForeignAlbumId,
                _ => null
            };
        }

        private MetadataSourceProfile ProfileForArtist(string foreignArtistId)
        {
            if (!_profileService.HasTaggedProfiles())
            {
                return _profileService.GetDefaultProfile();
            }

            var artist = _artistService.FindById(foreignArtistId);

            return artist == null ? _profileService.GetDefaultProfile() : _profileService.BestForTags(artist.Tags);
        }

        private MetadataSourceProfile ProfileForAlbum(string foreignAlbumId)
        {
            if (!_profileService.HasTaggedProfiles())
            {
                return _profileService.GetDefaultProfile();
            }

            var album = _albumService.FindById(foreignAlbumId);
            var artist = album == null ? null : _artistService.GetArtistByMetadataId(album.ArtistMetadataId);

            return artist == null ? _profileService.GetDefaultProfile() : _profileService.BestForTags(artist.Tags);
        }

        private Route RouteFor(MetadataSourceProfile profile, string foreignId)
        {
            var family = SourcesOf(profile).Select(x => x.Source).FirstOrDefault(s => s.CanHandleId(foreignId))?.IdFamily;

            if (family != null)
            {
                return FamilyRoute(profile, family);
            }

            if (_lastResort.CanHandleId(foreignId))
            {
                _logger.Debug("No source in metadata source profile {0} can look up {1}, using Lidarr's own server", profile.Name, foreignId);
                return LastResortRoute();
            }

            // Not an outage: the profile has no source for this kind of ID, so refresh keeps what it has
            throw new BadRequestException("No metadata source in profile {0} can look up {1}", profile.Name, foreignId);
        }

        private Route FamilyRoute(MetadataSourceProfile profile, string family)
        {
            var sources = SourcesOf(profile).Where(x => x.Source.IdFamily == family).ToList();

            if (sources.Empty())
            {
                return family == _lastResort.IdFamily ? LastResortRoute() : new Route();
            }

            return RouteOf(profile, sources);
        }

        private Route SearchRoute(string query)
        {
            var profile = _profileService.GetDefaultProfile();
            var family = IdFamilyOf(query);

            if (family != null)
            {
                return FamilyRoute(profile, family);
            }

            var sources = SourcesOf(profile);

            return sources.Empty() ? LastResortRoute() : RouteOf(profile, sources);
        }

        // Sent to other sources as plain text, an ID would bring back unrelated name matches
        private string IdFamilyOf(string query)
        {
            var id = query?.Trim() ?? string.Empty;

            if (SkyHookProxy.IsMbidQuery(id.ToLowerInvariant()))
            {
                return MetadataIdFamilies.MusicBrainz;
            }

            return EnabledSources().Values.FirstOrDefault(s => s.CanHandleId(id))?.IdFamily;
        }

        private static Route RouteOf(MetadataSourceProfile profile, List<(MetadataSourceRole Role, IMetadataSource Source)> sources)
        {
            return new Route
            {
                Primary = sources.Where(x => x.Role == MetadataSourceRole.Primary).Select(x => x.Source).ToList(),
                Fallback = sources.Where(x => x.Role == MetadataSourceRole.Fallback).Select(x => x.Source).ToList(),
                CombineAll = profile.PrimaryCombination == MetadataSourceCombination.And
            };
        }

        private Route LastResortRoute()
        {
            return new Route { Primary = new List<IMetadataSource> { _lastResort } };
        }

        private List<(MetadataSourceRole Role, IMetadataSource Source)> SourcesOf(MetadataSourceProfile profile)
        {
            var enabled = EnabledSources();

            return profile.Items
                .Where(i => enabled.ContainsKey(i.MetadataSourceId))
                .Select(i => (i.Role, enabled[i.MetadataSourceId]))
                .ToList();
        }

        private Dictionary<int, IMetadataSource> EnabledSources()
        {
            return _sourceCache.Get("enabled", () => _sourceFactory.GetAvailableProviders().ToDictionary(s => s.Definition.Id), TimeSpan.FromMinutes(5));
        }

        private class Route
        {
            public List<IMetadataSource> Primary { get; init; } = new();
            public List<IMetadataSource> Fallback { get; init; } = new();
            public bool CombineAll { get; init; }
        }
    }
}
