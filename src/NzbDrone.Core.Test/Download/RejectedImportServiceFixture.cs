using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RejectedImportServiceFixture : CoreTest<RejectedImportService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.ImportPending,
                DownloadItem = new DownloadClientItem { Title = "Artist Name - Album Title" },
                RemoteAlbum = new RemoteAlbum { Release = new ReleaseInfo { IndexerId = 3 } }
            };

            GivenFailDownloads();
        }

        private void GivenFailDownloads(params FailDownloads[] failDownloads)
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(s => s.GetSettings(3))
                  .Returns(new CachedIndexerSettings { FailDownloads = new HashSet<FailDownloads>(failDownloads) });
        }

        private static ImportResult Rejected(params ImportRejectionReason[] reasons)
        {
            return new ImportResult(new ImportDecision<LocalTrack>(null, reasons.Select(r => new ImportRejection(r, "Caution")).ToArray()), "Caution");
        }

        [TestCase(ImportRejectionReason.ExecutableFile, FailDownloads.Executables)]
        [TestCase(ImportRejectionReason.DangerousFile, FailDownloads.PotentiallyDangerous)]
        [TestCase(ImportRejectionReason.UserRejectedExtension, FailDownloads.UserDefinedExtensions)]
        public void should_fail_download_when_indexer_fails_that_file_type(ImportRejectionReason reason, FailDownloads failDownloads)
        {
            GivenFailDownloads(failDownloads);

            Subject.Process(_trackedDownload, Rejected(reason)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Error);
            _trackedDownload.DownloadItem.CanBeRemoved.Should().BeTrue();
        }

        [TestCase(ImportRejectionReason.ExecutableFile)]
        [TestCase(ImportRejectionReason.DangerousFile)]
        [TestCase(ImportRejectionReason.UserRejectedExtension)]
        public void should_warn_when_indexer_does_not_fail_that_file_type(ImportRejectionReason reason)
        {
            Subject.Process(_trackedDownload, Rejected(reason)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void should_warn_for_dangerous_file_when_only_executables_fail()
        {
            GivenFailDownloads(FailDownloads.Executables);

            Subject.Process(_trackedDownload, Rejected(ImportRejectionReason.DangerousFile)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [TestCase(FailDownloads.Executables)]
        [TestCase(FailDownloads.PotentiallyDangerous)]
        public void should_fail_a_folder_with_both_file_types_when_either_type_fails(FailDownloads failDownloads)
        {
            GivenFailDownloads(failDownloads);

            Subject.Process(_trackedDownload, Rejected(ImportRejectionReason.DangerousFile, ImportRejectionReason.ExecutableFile)).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailedPending);
        }

        [Test]
        public void should_warn_when_indexer_settings_are_unavailable()
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(s => s.GetSettings(3))
                  .Returns((CachedIndexerSettings)null);

            Subject.Process(_trackedDownload, Rejected(ImportRejectionReason.ExecutableFile)).Should().BeTrue();

            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void should_not_process_a_result_that_was_not_rejected()
        {
            Subject.Process(_trackedDownload, new ImportResult(new ImportDecision<LocalTrack>(new LocalTrack()))).Should().BeFalse();
        }

        [Test]
        public void should_not_process_a_download_without_a_release()
        {
            _trackedDownload.RemoteAlbum = null;

            Subject.Process(_trackedDownload, Rejected(ImportRejectionReason.ExecutableFile)).Should().BeFalse();
        }
    }
}
