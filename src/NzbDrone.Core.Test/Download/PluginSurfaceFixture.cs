using System;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class PluginSurfaceFixture
    {
        [TestCase(typeof(IConfigService), "get_UserRejectedExtensions")]
        [TestCase(typeof(IConfigService), "set_UserRejectedExtensions")]
        [TestCase(typeof(ITorrentIndexerSettings), "get_RejectTorrentFilesWithBlockedExtensionsWhileGrabbing")]
        [TestCase(typeof(ITorrentIndexerSettings), "set_RejectTorrentFilesWithBlockedExtensionsWhileGrabbing")]
        [TestCase(typeof(ITorrentFileInfoReader), "GetFileNamesFromTorrentFile")]
        public void should_give_members_added_to_develop_interfaces_a_default(Type type, string method)
        {
            type.GetMethod(method).IsAbstract.Should().BeFalse();
        }

        [Test]
        public void should_keep_develops_downloaded_tracks_import_service_constructor()
        {
            typeof(DownloadedTracksImportService).GetConstructor(new[]
            {
                typeof(IDiskProvider),
                typeof(IDiskScanService),
                typeof(IArtistService),
                typeof(IParsingService),
                typeof(IMakeImportDecision),
                typeof(IImportApprovedTracks),
                typeof(IEventAggregator),
                typeof(IRuntimeInfo),
                typeof(Logger)
            }).Should().NotBeNull();
        }

        [Test]
        public void should_keep_develops_completed_download_service_constructor()
        {
            typeof(CompletedDownloadService).GetConstructor(new[]
            {
                typeof(IEventAggregator),
                typeof(IHistoryService),
                typeof(IProvideImportItemService),
                typeof(IDownloadedTracksImportService),
                typeof(IArtistService),
                typeof(IParsingService),
                typeof(ITrackedDownloadAlreadyImported),
                typeof(Logger)
            }).Should().NotBeNull();
        }
    }
}
