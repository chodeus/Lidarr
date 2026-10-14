using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Blocklisting
{
    public class UsenetBlocklist : IBlocklistForProtocol
    {
        private readonly IBlocklistRepository _blocklistRepository;

        public UsenetBlocklist(IBlocklistRepository blocklistRepository)
        {
            _blocklistRepository = blocklistRepository;
        }

        public string Protocol => nameof(UsenetDownloadProtocol);

        public bool IsBlocklisted(int artistId, ReleaseInfo release)
        {
            return _blocklistRepository.BlocklistedByTitle(artistId, release.Title)
                .Where(b => b.Protocol == Protocol)
                .Any(b => SameNzb(b, release));
        }

        public Blocklist GetBlocklist(DownloadFailedEvent message)
        {
            return new Blocklist
            {
                ArtistId = message.ArtistId,
                AlbumIds = message.AlbumIds,
                SourceTitle = message.SourceTitle,
                Quality = message.Quality,
                Date = DateTime.UtcNow,
                PublishedDate = DateTime.Parse(message.Data.GetValueOrDefault("publishedDate")),
                Size = long.Parse(message.Data.GetValueOrDefault("size", "0")),
                Indexer = message.Data.GetValueOrDefault("indexer"),
                Protocol = message.Data.GetValueOrDefault("protocol"),
                Message = message.Message,
            };
        }

        private bool SameNzb(Blocklist item, ReleaseInfo release)
        {
            if (!HasSameIndexer(item, release.Indexer))
            {
                return false;
            }

            return item.PublishedDate == release.PublishDate;
        }

        private bool HasSameIndexer(Blocklist item, string indexer)
        {
            if (item.Indexer.IsNullOrWhiteSpace())
            {
                return true;
            }

            return item.Indexer.Equals(indexer, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
