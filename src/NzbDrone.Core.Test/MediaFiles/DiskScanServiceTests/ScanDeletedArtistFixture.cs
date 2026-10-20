using System.Collections.Generic;
using System.IO.Abstractions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    // The real ArtistService over the test database; the disk, import decisions and media files are mocked
    [TestFixture]
    public class ScanDeletedArtistFixture : DbTest<DiskScanService, Artist>
    {
        private string _rootFolder;
        private Artist _kept;
        private Artist _deleted;
        private List<ArtistScanSkippedEvent> _skipped;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());

            _rootFolder = "/music".AsOsAgnostic();
            _kept = GivenArtist("/music/Kept Artist");
            _deleted = GivenArtist("/music/Deleted Artist");
            Mocker.Resolve<IArtistService>().DeleteArtist(_deleted.Id, false);

            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder { Path = _rootFolder });
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(new List<ImportDecision<LocalTrack>>());
            Mocker.GetMock<IMediaFileService>().Setup(v => v.GetFilesWithBasePath(It.IsAny<string>())).Returns(new List<TrackFile>());
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfos(It.IsAny<string>(), true)).Returns(new List<IFileInfo>());

            _skipped = new List<ArtistScanSkippedEvent>();
            Mocker.GetMock<IEventAggregator>().Setup(s => s.PublishEvent(It.IsAny<ArtistScanSkippedEvent>())).Callback<ArtistScanSkippedEvent>(e => _skipped.Add(e));
        }

        private Artist GivenArtist(string path)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = path).Build();
            Db.Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                                        .With(a => a.Id = 0)
                                        .With(a => a.ArtistMetadataId = metadata.Id)
                                        .With(a => a.Path = path.AsOsAgnostic())
                                        .Build();

            Db.Insert(artist);

            return artist;
        }

        [Test]
        public void should_scan_the_stored_artists_when_one_was_deleted_after_the_scan_was_queued()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_rootFolder)).Returns(true);

            Subject.Scan(new List<string> { _rootFolder }, FilterFilesType.Known, false, new List<int> { _deleted.Id, _kept.Id });

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IDiskProvider>().Verify(s => s.FolderExists(_kept.Path), Times.Once());
            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        // A delete through the API runs on a request thread, so it can land while the scan reads the disk
        [Test]
        public void should_scan_the_stored_artists_when_one_is_deleted_while_the_folders_are_read()
        {
            var other = GivenArtist("/music/Other Artist");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_rootFolder)).Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFileInfos(_rootFolder, true))
                  .Callback(() => Mocker.Resolve<IArtistService>().DeleteArtist(other.Id, false))
                  .Returns(new List<IFileInfo>());

            Subject.Scan(new List<string> { _rootFolder }, FilterFilesType.Known, false, new List<int> { _kept.Id, other.Id });

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_report_only_stored_artists_as_skipped_when_the_root_folder_is_missing()
        {
            Subject.Scan(new List<string> { _kept.Path }, FilterFilesType.Known, false, new List<int> { _deleted.Id, _kept.Id, _kept.Id });

            ExceptionVerification.ExpectedWarns(1);
            _skipped.Should().ContainSingle().Which.Artist.Id.Should().Be(_kept.Id);
        }

        [Test]
        public void should_scan_an_artist_named_twice_once()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_rootFolder)).Returns(true);

            Subject.Scan(new List<string> { _rootFolder }, FilterFilesType.Known, false, new List<int> { _kept.Id, _kept.Id });

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IDiskProvider>().Verify(s => s.FolderExists(_kept.Path), Times.Once());
        }
    }
}
