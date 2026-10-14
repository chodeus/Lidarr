using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Blocklisting
{
    [TestFixture]
    public class UsenetBlocklistFixture : CoreTest<UsenetBlocklist>
    {
        private const int ArtistId = 1;
        private const string Title = "Artist Name - Album Title (2026) [FLAC]";
        private const long Size = 1352581934;

        private static readonly DateTime PublishedDate = new DateTime(2026, 2, 6, 15, 39, 0, DateTimeKind.Utc);

        private void GivenBlocklisted(string indexer, DateTime? publishedDate)
        {
            Mocker.GetMock<IBlocklistRepository>()
                .Setup(s => s.BlocklistedByTitle(ArtistId, Title))
                .Returns(new List<Blocklist>
                {
                    new Blocklist
                    {
                        ArtistId = ArtistId,
                        SourceTitle = Title,
                        Indexer = indexer,
                        PublishedDate = publishedDate,
                        Size = Size,
                        Protocol = nameof(UsenetDownloadProtocol)
                    }
                });
        }

        private bool IsBlocklisted(string indexer, DateTime publishDate)
        {
            return Subject.IsBlocklisted(ArtistId, new ReleaseInfo { Title = Title, Indexer = indexer, PublishDate = publishDate, Size = Size });
        }

        [Test]
        public void should_not_match_release_from_another_indexer_with_near_published_date()
        {
            GivenBlocklisted("Indexer One", PublishedDate);

            IsBlocklisted("Indexer Two", PublishedDate.AddSeconds(-10)).Should().BeFalse();
        }

        [Test]
        public void should_not_match_release_from_another_indexer_with_identical_published_date()
        {
            GivenBlocklisted("Indexer One", PublishedDate);

            IsBlocklisted("Indexer Two", PublishedDate).Should().BeFalse();
        }

        [TestCase("Indexer One")]
        [TestCase("INDEXER ONE")]
        public void should_match_same_indexer_with_identical_published_date(string indexer)
        {
            GivenBlocklisted("Indexer One", PublishedDate);

            IsBlocklisted(indexer, PublishedDate).Should().BeTrue();
        }

        [Test]
        public void should_not_match_same_indexer_when_published_date_differs()
        {
            GivenBlocklisted("Indexer One", PublishedDate);

            IsBlocklisted("Indexer One", PublishedDate.AddSeconds(-10)).Should().BeFalse();
        }

        [Test]
        public void should_not_match_same_indexer_when_item_has_no_published_date()
        {
            GivenBlocklisted("Indexer One", null);

            IsBlocklisted("Indexer One", PublishedDate).Should().BeFalse();
        }

        [Test]
        public void should_match_item_without_indexer_with_identical_published_date()
        {
            GivenBlocklisted(null, PublishedDate);

            IsBlocklisted("Indexer Two", PublishedDate).Should().BeTrue();
        }

        [Test]
        public void should_not_match_item_without_indexer_when_published_date_differs()
        {
            GivenBlocklisted(null, PublishedDate);

            IsBlocklisted("Indexer Two", PublishedDate.AddSeconds(-10)).Should().BeFalse();
        }
    }
}
