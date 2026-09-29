using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Music.ArtistCredits
{
    public interface IAlbumArtistCreditService
    {
        AlbumArtistCredit GetForAlbum(int albumId);
        List<AlbumArtistCredit> GetForAlbums(List<int> albumIds);
    }

    public class AlbumArtistCreditService : IAlbumArtistCreditService,
        IExecute<RefreshAlbumArtistCreditsCommand>,
        IHandle<AlbumDeletedEvent>,
        IHandle<ApplicationStartedEvent>,
        IHandle<ApplicationShutdownRequested>
    {
        // Sized to finish inside the 15-minute task interval at one request per 1.1 seconds.
        public const int BatchSize = 700;
        public const int MaxConsecutiveFailures = 5;
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(90);

        private readonly IAlbumArtistCreditRepository _repository;
        private readonly IMusicBrainzArtistCreditProxy _proxy;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        private volatile bool _stopping;

        public AlbumArtistCreditService(IAlbumArtistCreditRepository repository,
                                        IMusicBrainzArtistCreditProxy proxy,
                                        IConfigService configService,
                                        Logger logger)
        {
            _repository = repository;
            _proxy = proxy;
            _configService = configService;
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
            var candidates = _repository.GetCandidates(DateTime.UtcNow - RefreshAfter, BatchSize);
            if (candidates.Count == 0)
            {
                return;
            }

            var existing = _repository.FindByAlbumIds(candidates.Select(c => c.AlbumId).ToList()).ToDictionary(c => c.AlbumId);
            var stored = 0;
            var failures = 0;

            foreach (var candidate in candidates)
            {
                if (_stopping)
                {
                    break;
                }

                var names = _proxy.GetCredit(candidate.ForeignAlbumId);

                // A failed read is retried on a later run, never stored as an empty credit.
                if (names == null)
                {
                    if (++failures >= MaxConsecutiveFailures)
                    {
                        _logger.Warn("MusicBrainz is not answering; stopping the artist credit refresh after {0} of {1} albums", stored, candidates.Count);
                        break;
                    }

                    continue;
                }

                failures = 0;

                var credit = AlbumArtistCredit.For(candidate.AlbumId, names, candidate.ForeignArtistId, DateTime.UtcNow);
                if (existing.TryGetValue(candidate.AlbumId, out var current))
                {
                    credit.Id = current.Id;
                }

                _repository.Upsert(credit);
                stored++;
            }

            _logger.Info("Refreshed the artist credit of {0} of {1} albums", stored, candidates.Count);
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
