using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class DownloadedTracksImportGrabbedAlbumFixture : FileSystemTest<DownloadedTracksImportService>
    {
        private readonly string _folder = "c:\\drop\\Artist - Album".AsOsAgnostic();
        private readonly string _file = "c:\\drop\\Artist - Album\\01 - Track.flac".AsOsAgnostic();
        private readonly Artist _artist = new Artist { Id = 1 };
        private readonly Album _album = new Album { Id = 7 };
        private readonly DownloadClientItem _item = new DownloadClientItem { DownloadId = "grab-1", CanMoveFiles = true };

        private IdentificationOverrides _overrides;
        private ImportDecisionMakerConfig _config;

        [SetUp]
        public void Setup()
        {
            FileSystem.AddFile(_file, new MockFileData("".PadRight(10)));

            Mocker.GetMock<IDiskScanService>()
                .Setup(c => c.GetAudioFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new[] { DiskProvider.GetFileInfo(_file) });

            Mocker.GetMock<IDiskScanService>()
                .Setup(c => c.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                .Returns<string, IEnumerable<IFileInfo>>((b, s) => s.ToList());

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) =>
                {
                    _overrides = o;
                    _config = c;
                })
                .Returns(new List<ImportDecision<LocalTrack>>());

            Mocker.GetMock<IImportApprovedTracks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalTrack>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns(new List<ImportResult>());
        }

        [Test]
        public void should_judge_a_grabbed_albums_folder_as_one_release_of_that_album()
        {
            Subject.ProcessGrabbedPath(_folder, ImportMode.Auto, _artist, _album, _item);

            _overrides.Artist.Should().BeSameAs(_artist);
            _overrides.Album.Should().BeSameAs(_album);
            _config.SingleRelease.Should().BeTrue();
        }

        [Test]
        public void should_judge_a_grabbed_file_against_that_album()
        {
            Subject.ProcessGrabbedPath(_file, ImportMode.Auto, _artist, _album, _item);

            _overrides.Album.Should().BeSameAs(_album);
        }

        [Test]
        public void should_keep_develops_identification_without_an_album()
        {
            Subject.ProcessPath(_folder, ImportMode.Auto, _artist, _item);

            _overrides.Artist.Should().BeSameAs(_artist);
            _overrides.Album.Should().BeNull();
            _config.SingleRelease.Should().BeFalse();
        }

        // Plugins built against develop implement or mock this interface without the new member.
        [Test]
        public void should_give_the_new_interface_member_a_default_body()
        {
            typeof(IDownloadedTracksImportService).GetMethod(nameof(IDownloadedTracksImportService.ProcessGrabbedPath))
                .IsAbstract.Should().BeFalse();
        }
    }
}
