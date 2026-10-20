using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // The real ArtistService and metadata repository over the test database; the lookup and the album refresh are mocked
    [TestFixture]
    public class RefreshArtistDeletedStatusFixture : DbTest<RefreshArtistService, Artist>
    {
        private ArtistMetadata _metadata;
        private Artist _artist;
        private ArtistService _artistService;
        private Action _beforeStatusWrite;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            var metadataRepository = Mocker.Resolve<ArtistMetadataRepository>();
            Mocker.SetConstant<IArtistMetadataRepository>(metadataRepository);
            Mocker.SetConstant<IArtistMetadataService>(Mocker.Resolve<ArtistMetadataService>());

            // Writes go to the real repository, after _beforeStatusWrite, which stands for another reader
            _beforeStatusWrite = () => { };
            var statusWrites = new Mock<IArtistMetadataRepository>();
            statusWrites.Setup(r => r.SetFields(It.IsAny<ArtistMetadata>(), It.IsAny<Expression<Func<ArtistMetadata, object>>[]>()))
                        .Callback<ArtistMetadata, Expression<Func<ArtistMetadata, object>>[]>((metadata, fields) =>
                        {
                            _beforeStatusWrite();
                            metadataRepository.SetFields(metadata, fields);
                        });
            _artistService = new ArtistService(Mocker.Resolve<IArtistRepository>(), statusWrites.Object, Mocker.GetMock<IEventAggregator>().Object, Mocker.GetMock<IBuildArtistPaths>().Object, Mocker.Resolve<ICacheManager>(), TestLogger);
            Mocker.SetConstant<IArtistService>(_artistService);
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            _metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = "artist-1").With(m => m.Status = ArtistStatusType.Continuing).Build();
            Db.Insert(_metadata);

            _artist = Builder<Artist>.CreateNew()
                                     .With(a => a.Id = 0)
                                     .With(a => a.ArtistMetadataId = _metadata.Id)
                                     .With(a => a.Path = "/music/Artist Name".AsOsAgnostic())
                                     .With(a => a.Tags = new HashSet<int>())
                                     .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                     .Build();
            Db.Insert(_artist);

            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByForeignId(It.IsAny<List<string>>())).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.GetTagChanges(It.IsAny<Artist>())).Returns(new AutoTaggingChanges());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByArtist(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>())).Returns(new List<EntityHistory>());
            Mocker.GetMock<ILocalizationService>().Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>())).Returns("removed");
        }

        private void GivenTheLookupFindsNoArtist()
        {
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Throws(new ArtistNotFoundException("artist-1"));
        }

        private ArtistStatusType StoredStatus()
        {
            return Mocker.Resolve<IArtistMetadataRepository>().Get(_metadata.Id).Status;
        }

        [Test]
        public void should_store_the_deleted_status_when_the_metadata_source_no_longer_has_the_artist()
        {
            GivenTheLookupFindsNoArtist();

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            StoredStatus().Should().Be(ArtistStatusType.Deleted);
        }

        [Test]
        public void should_report_the_artist_in_the_removed_artist_health_check_at_once()
        {
            _artistService.GetAllArtists();
            GivenTheLookupFindsNoArtist();

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            Mocker.Resolve<RemovedArtistCheck>().Check().Reason.Should().Be(HealthCheckReason.RemovedArtistSingle);
        }

        // Another reader, such as the artist list in the UI, filling the cache while the status is written
        [Test]
        public void should_report_the_artist_when_the_artist_list_is_read_while_the_status_is_written()
        {
            _beforeStatusWrite = () => _artistService.GetAllArtists();
            GivenTheLookupFindsNoArtist();

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            Mocker.Resolve<RemovedArtistCheck>().Check().Reason.Should().Be(HealthCheckReason.RemovedArtistSingle);
        }

        [Test]
        public void should_clear_the_deleted_status_when_the_metadata_source_has_the_artist_again()
        {
            GivenTheLookupFindsNoArtist();
            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Returns(() => new Artist
                  {
                      CleanName = "artistname",
                      SortName = "artist name",
                      Metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = "artist-1").With(m => m.Status = ArtistStatusType.Continuing).Build(),
                      Albums = new List<Album>()
                  });

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            StoredStatus().Should().Be(ArtistStatusType.Continuing);
        }
    }
}
