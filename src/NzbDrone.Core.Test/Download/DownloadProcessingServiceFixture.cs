using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadProcessingServiceFixture : CoreTest<DownloadProcessingService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownload
            {
                State = TrackedDownloadState.ImportPending,
                IsTrackable = true,
                DownloadItem = new DownloadClientItem { Title = "Artist Name - Album Title" }
            };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(new List<TrackedDownload> { _trackedDownload });
        }

        [Test]
        public void should_process_download_failed_during_import_in_the_same_pass()
        {
            Mocker.GetMock<ICompletedDownloadService>()
                  .Setup(s => s.Import(_trackedDownload))
                  .Callback(() => _trackedDownload.Fail());

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<IFailedDownloadService>()
                  .Verify(s => s.ProcessFailed(_trackedDownload), Times.Once());
        }

        [Test]
        public void should_process_failed_pending_download_without_importing_it()
        {
            _trackedDownload.State = TrackedDownloadState.DownloadFailedPending;

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>()
                  .Verify(s => s.Import(It.IsAny<TrackedDownload>()), Times.Never());

            Mocker.GetMock<IFailedDownloadService>()
                  .Verify(s => s.ProcessFailed(_trackedDownload), Times.Once());
        }

        [Test]
        public void should_not_process_imported_download_as_failed()
        {
            Mocker.GetMock<ICompletedDownloadService>()
                  .Setup(s => s.Import(_trackedDownload))
                  .Callback(() => _trackedDownload.State = TrackedDownloadState.Imported);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<IFailedDownloadService>()
                  .Verify(s => s.ProcessFailed(It.IsAny<TrackedDownload>()), Times.Never());
        }
    }
}
