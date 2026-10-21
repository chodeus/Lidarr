using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    // The real ImportApprovedTracks, so the import mode reaches the file upgrader as it does live; the upgrader and the database are faked
    [TestFixture]
    public class ManualImportUnknownDownloadFixture : CoreTest<ManualImportService>
    {
        [SetUp]
        public void Setup()
        {
            var artist = Builder<Artist>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = "/music/Artist Name".AsOsAgnostic())
                .With(a => a.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .Build();
            var release = Builder<AlbumRelease>.CreateNew().With(r => r.Id = 1).Build();
            var album = Builder<Album>.CreateNew().With(a => a.Id = 1).Build();
            album.AlbumReleases = new List<AlbumRelease> { release };

            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.Length).Returns(1000);
            file.SetupGet(f => f.LastWriteTimeUtc).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(It.IsAny<int>())).Returns(artist);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(It.IsAny<int>())).Returns(album);
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(It.IsAny<int>())).Returns(release);
            Mocker.GetMock<IReleaseService>().Setup(s => s.SetMonitored(It.IsAny<AlbumRelease>())).Returns(new List<AlbumRelease> { release });
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(Builder<Track>.CreateListOfSize(1).Build() as List<Track>);
            Mocker.GetMock<IAudioTagService>().Setup(s => s.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolder(It.IsAny<string>())).Returns(new RootFolder());
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileInfo(It.IsAny<string>())).Returns(file.Object);
            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns(new TrackFileMoveResult());

            Mocker.SetConstant<IImportApprovedTracks>(Mocker.Resolve<ImportApprovedTracks>());
        }

        private void Execute(ImportMode importMode)
        {
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find(It.IsAny<string>())).Returns((TrackedDownload)null);

            Subject.Execute(new ManualImportCommand
            {
                ImportMode = importMode,
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = "/downloads/Artist Name - Album Title/01 - Track Title.flac".AsOsAgnostic(),
                        ArtistId = 1,
                        AlbumId = 1,
                        AlbumReleaseId = 1,
                        TrackIds = new List<int> { 1 },
                        Quality = new QualityModel(Quality.FLAC),
                        DownloadId = "download-not-tracked"
                    }
                }
            });
        }

        // The tracked-download cache can miss a download still seeding, such as after a restart
        [Test]
        public void should_copy_files_of_a_download_it_cannot_find_when_the_mode_is_auto()
        {
            Execute(ImportMode.Auto);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), true), Times.Once());
        }

        [Test]
        public void should_move_files_of_a_download_it_cannot_find_when_asked_to()
        {
            Execute(ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), false), Times.Once());
        }

        // The API's enum converter accepts an integer, and ImportApprovedTracks treats an undefined mode as Auto
        [Test]
        public void should_copy_files_of_a_download_it_cannot_find_when_the_mode_is_undefined()
        {
            Execute((ImportMode)3);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), true), Times.Once());
        }

        [Test]
        public void should_copy_the_file_of_an_unknown_download_imported_with_a_tracked_one()
        {
            var tracked = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { DownloadId = "tracked", Title = "Tracked", CanMoveFiles = true, Status = DownloadItemStatus.Downloading }
            };

            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("tracked")).Returns(tracked);
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.Find("download-not-tracked")).Returns((TrackedDownload)null);
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId(It.IsAny<string>())).Returns(new List<EntityHistory>());
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns((IEnumerable<int> ids) => ids.Select(id => new Track { Id = id }).ToList());

            Subject.Execute(new ManualImportCommand
            {
                ImportMode = ImportMode.Auto,
                Files = new List<ManualImportFile> { GivenFile("01.flac", "tracked", 1), GivenFile("02.flac", "download-not-tracked", 2) }
            });

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()), Times.Exactly(2));
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.Is<LocalTrack>(l => l.Path.Contains("download-not-tracked")), true), Times.Once());
        }

        private static ManualImportFile GivenFile(string name, string downloadId, int trackId)
        {
            return new ManualImportFile
            {
                Path = $"/downloads/{downloadId}/{name}".AsOsAgnostic(),
                ArtistId = 1,
                AlbumId = 1,
                AlbumReleaseId = 1,
                TrackIds = new List<int> { trackId },
                Quality = new QualityModel(Quality.FLAC),
                DownloadId = downloadId
            };
        }
    }
}
