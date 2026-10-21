using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public interface IAlbumArtistCreditService
    {
        AlbumArtistCredit GetForAlbum(int albumId);
        List<AlbumArtistCredit> GetForAlbums(List<int> albumIds);
    }

    public class AlbumArtistCreditService : IAlbumArtistCreditService,
        IExecute<RefreshAlbumArtistCreditsCommand>,
        IHandle<AlbumAddedEvent>,
        IHandle<AlbumInfoRefreshedEvent>,
        IHandle<AlbumDeletedEvent>,
        IHandle<ApplicationStartedEvent>,
        IHandle<ApplicationShutdownRequested>
    {
        // Sized to finish inside the 15-minute task interval at one request per 1.1 seconds.
        public const int BatchSize = 700;
        public const int MaxConsecutiveFailures = 5;
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(90);

        // A failed album waits this long, so albums that always fail can't hold the front of the queue
        public static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(6);

        private readonly IAlbumArtistCreditRepository _repository;
        private readonly IMusicBrainzArtistCreditProxy _proxy;
        private readonly IConfigService _configService;
        private readonly INamingConfigService _namingConfigService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<int, DateTime> _retryAfter = new ConcurrentDictionary<int, DateTime>();
        private readonly object _writeLock = new object();

        private volatile bool _stopping;

        public AlbumArtistCreditService(IAlbumArtistCreditRepository repository,
                                        IMusicBrainzArtistCreditProxy proxy,
                                        IConfigService configService,
                                        INamingConfigService namingConfigService,
                                        IManageCommandQueue commandQueueManager,
                                        Logger logger)
        {
            _repository = repository;
            _proxy = proxy;
            _configService = configService;
            _namingConfigService = namingConfigService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public AlbumArtistCredit GetForAlbum(int albumId)
        {
            return _repository.FindByAlbumId(albumId);
        }

        public List<AlbumArtistCredit> GetForAlbums(List<int> albumIds)
        {
            return _repository.FindByAlbumIds(albumIds);
        }

        public void Execute(RefreshAlbumArtistCreditsCommand message)
        {
            var started = DateTime.UtcNow;
            foreach (var expired in _retryAfter.Where(r => r.Value <= started).ToList())
            {
                _retryAfter.TryRemove(expired.Key, out _);
            }

            // Only the scheduled run backs off: a run for just-added albums is there to beat their downloads
            var scheduled = message.AlbumIds?.Any() != true;
            var candidates = (scheduled
                    ? _repository.GetCandidates(started - RefreshAfter, BatchSize + _retryAfter.Count).Where(c => !_retryAfter.ContainsKey(c.AlbumId))
                    : _repository.GetUncreditedCandidates(message.AlbumIds))
                .Take(BatchSize)
                .ToList();

            if (candidates.Count == 0)
            {
                return;
            }

            var naming = _namingConfigService.GetConfig();
            var namesUseGuests = new[] { naming.StandardTrackFormat, naming.MultiDiscTrackFormat }.Any(f => f?.IndexOf("Album Guests", StringComparison.OrdinalIgnoreCase) >= 0);
            var stored = 0;
            var failures = 0;

            foreach (var candidate in candidates)
            {
                if (_stopping)
                {
                    break;
                }

                // Another run, scheduled or for just-added albums, may have fetched it since this one started
                if (_repository.FindByAlbumId(candidate.AlbumId)?.LastFetched >= started)
                {
                    continue;
                }

                var names = _proxy.GetCredit(candidate.ForeignAlbumId);

                // A failed read is retried on a later run, never stored as an empty credit.
                if (names == null)
                {
                    if (scheduled)
                    {
                        _retryAfter[candidate.AlbumId] = DateTime.UtcNow + RetryFailedAfter;
                    }

                    if (++failures >= MaxConsecutiveFailures)
                    {
                        _logger.Warn("MusicBrainz is not answering; stopping the artist credit refresh after {0} of {1} albums", stored, candidates.Count);
                        break;
                    }

                    continue;
                }

                failures = 0;

                var credit = AlbumArtistCredit.For(candidate.AlbumId, names, candidate.ForeignArtistId, DateTime.UtcNow);
                AlbumArtistCredit current;

                // Read again just before writing: another run may have stored this album during the fetch
                lock (_writeLock)
                {
                    current = _repository.FindStoredByAlbumId(candidate.AlbumId);
                    credit.Id = current?.Id ?? 0;
                    _repository.Upsert(credit);
                }

                stored++;

                // Files keep their names and tags until renamed or retagged, as after a naming change
                var guestsChanged = namesUseGuests && (current?.Guests ?? string.Empty) != credit.Guests;
                var creditChanged = _configService.WriteAlbumArtistCredits && current != null && current.Credit != credit.Credit;
                if (candidate.HasFiles && (guestsChanged || creditChanged))
                {
                    _logger.Warn("The artist credit of album {0} is now '{1}' (guests '{2}'); rename or retag its files to use it", candidate.AlbumId, credit.Credit, credit.Guests);
                }
            }

            _logger.Info("Refreshed the artist credit of {0} of {1} albums", stored, candidates.Count);
        }

        // Folders of same-titled albums collide without their guests, so their credits are fetched before a download can finish
        public void Handle(AlbumAddedEvent message)
        {
            FetchSoon(new List<int> { message.Album.Id });
        }

        // An artist added or refreshed stores its new albums together, without an AlbumAddedEvent for each
        public void Handle(AlbumInfoRefreshedEvent message)
        {
            FetchSoon(message.Added.Select(a => a.Id).ToList());
        }

        private void FetchSoon(List<int> albumIds)
        {
            var albums = albumIds.Any() ? _repository.GetAlbumsWithSiblings(albumIds) : new List<int>();
            if (albums.Any())
            {
                _commandQueueManager.Push(new RefreshAlbumArtistCreditsCommand { AlbumIds = albums });
            }
        }

        public void Handle(AlbumDeletedEvent message)
        {
            _repository.DeleteByAlbumId(message.Album.Id);
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _stopping = true;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            AlbumArtistCreditLookup.Configure(GetForAlbum, () => _configService.WriteAlbumArtistCredits);
        }
    }
}
