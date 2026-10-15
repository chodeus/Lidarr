using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class ReleaseSearchServiceFixture : CoreTest<ReleaseSearchService>
    {
        private Artist _artist;
        private AlbumSearchCriteria _searched;

        [SetUp]
        public void Setup()
        {
            _artist = new Artist { Id = 1, Name = "Artist Name", Tags = new HashSet<int>() };

            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition { Id = 1, Tags = new HashSet<int>() });
            indexer.Setup(s => s.Fetch(It.IsAny<AlbumSearchCriteria>()))
                   .Callback<AlbumSearchCriteria>(c => _searched = c)
                   .ReturnsAsync(new List<ReleaseInfo>());

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(_artist.Id)).Returns(_artist);
            Mocker.GetMock<IIndexerFactory>().Setup(s => s.AutomaticSearchEnabled(It.IsAny<bool>())).Returns(new List<IIndexer> { indexer.Object });
            Mocker.GetMock<IMakeDownloadDecision>()
                  .Setup(s => s.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(new List<DownloadDecision>());
        }

        private async Task<AlbumSearchCriteria> SearchForAlbumDated(DateTime releaseDate)
        {
            await Subject.AlbumSearch(new Album { Id = 2, ArtistId = _artist.Id, Title = "Album Title", ReleaseDate = releaseDate }, false, false, false);

            return _searched;
        }

        [Test]
        public async Task should_search_with_the_albums_year()
        {
            (await SearchForAlbumDated(new DateTime(2019, 5, 3))).AlbumYear.Should().Be(2019);
        }

        [Test]
        public async Task should_search_without_a_year_for_an_album_dated_year_one()
        {
            (await SearchForAlbumDated(new DateTime(1, 1, 1))).AlbumYear.Should().Be(0);
        }
    }
}
