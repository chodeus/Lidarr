using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private const string ReleaseTitle = "Artist Name - Single Name (2024) [WEB] [FLAC]";

        private Artist _artist;
        private List<ImportDecision<LocalTrack>> _imported;
        private ImportDecisionMakerConfig _decisionConfig;

        [SetUp]
        public void Setup()
        {
            _artist = new Artist { Id = 1, Path = @"C:\Music\Artist Name".AsOsAgnostic() };
            _imported = new List<ImportDecision<LocalTrack>>();

            Mocker.GetMock<IArtistService>().Setup(s => s.GetArtist(1)).Returns(_artist);
            Mocker.GetMock<IAlbumService>().Setup(s => s.GetAlbum(It.IsAny<int>())).Returns(new Album());
            Mocker.GetMock<IReleaseService>().Setup(s => s.GetRelease(It.IsAny<int>())).Returns(new AlbumRelease());
            Mocker.GetMock<ITrackService>().Setup(s => s.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(new List<Track>());
            Mocker.GetMock<IAudioTagService>().Setup(s => s.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFileInfo(It.IsAny<string>()))
                  .Returns<string>(path => Mock.Of<IFileInfo>(f => f.FullName == path && f.Length == 10 && f.LastWriteTimeUtc == DateTime.UtcNow));

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolder(It.Is<string>(p => p.StartsWith(@"C:\Music".AsOsAgnostic()))))
                  .Returns(new RootFolder { Path = @"C:\Music".AsOsAgnostic() });

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.Find("download-1"))
                  .Returns(new TrackedDownload { DownloadItem = new DownloadClientItem { DownloadId = "download-1", Title = ReleaseTitle } });

            Mocker.GetMock<IImportApprovedTracks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Callback<List<ImportDecision<LocalTrack>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) => _imported.AddRange(decisions))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetAudioFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(Array.Empty<IFileInfo>());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) => _decisionConfig = config)
                  .Returns(new List<ImportDecision<LocalTrack>>());
        }

        private static ManualImportFile File(string path, string downloadId = "download-1") => new ManualImportFile
        {
            Path = path.AsOsAgnostic(),
            ArtistId = 1,
            AlbumId = 1,
            AlbumReleaseId = 1,
            TrackIds = new List<int> { 1 },
            Quality = new QualityModel(Quality.FLAC),
            DownloadId = downloadId
        };

        private void Execute(params ManualImportFile[] files)
        {
            Subject.Execute(new ManualImportCommand { Files = files.ToList(), ImportMode = ImportMode.Move });
        }

        private void GivenFolder(string folder)
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(folder)).Returns(true);
        }

        [Test]
        public void should_keep_the_release_title_for_a_single_file_download()
        {
            Execute(File(@"C:\Downloads\Single Name\01 - Single Name.flac"));

            _imported.Should().ContainSingle().Which.Item.SceneName.Should().Be(ReleaseTitle);
        }

        [Test]
        public void should_not_give_each_track_of_an_album_download_its_release_title()
        {
            Execute(File(@"C:\Downloads\Album Name\01 - First.flac"), File(@"C:\Downloads\Album Name\02 - Second.flac"));

            _imported.Should().HaveCount(2).And.OnlyContain(x => x.Item.SceneName == null);
        }

        [Test]
        public void should_not_set_a_scene_name_for_a_file_already_in_the_artist_folder()
        {
            Execute(File(@"C:\Music\Artist Name\Single Name\01 - Single Name.flac"));

            _imported.Should().ContainSingle().Which.Item.SceneName.Should().BeNull();
        }

        [Test]
        public void should_treat_a_folder_outside_the_artist_folder_as_a_scene_source()
        {
            var folder = @"C:\Downloads\Single Name".AsOsAgnostic();
            GivenFolder(folder);

            Subject.GetMediaFiles(folder, null, _artist, FilterFilesType.None, false);

            _decisionConfig.SceneSource.Should().BeTrue();
        }

        [Test]
        public void should_not_treat_the_artist_folder_as_a_scene_source()
        {
            var folder = @"C:\Music\Artist Name\Single Name".AsOsAgnostic();
            GivenFolder(folder);

            Subject.GetMediaFiles(folder, null, _artist, FilterFilesType.None, false);

            _decisionConfig.SceneSource.Should().BeFalse();
        }

        [Test]
        public void should_treat_a_folder_outside_every_root_folder_as_a_scene_source_when_the_artist_is_unknown()
        {
            var folder = @"C:\Downloads\Single Name".AsOsAgnostic();
            GivenFolder(folder);

            Subject.GetMediaFiles(folder, null, null, FilterFilesType.None, false);

            _decisionConfig.SceneSource.Should().BeTrue();
        }

        [Test]
        public void should_not_treat_a_root_folder_as_a_scene_source_when_the_artist_is_unknown()
        {
            var folder = @"C:\Music\Unsorted".AsOsAgnostic();
            GivenFolder(folder);

            Subject.GetMediaFiles(folder, null, null, FilterFilesType.None, false);

            _decisionConfig.SceneSource.Should().BeFalse();
        }
    }
}
