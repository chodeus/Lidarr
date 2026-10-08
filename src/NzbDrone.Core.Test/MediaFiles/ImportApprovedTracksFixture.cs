using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedTracksFixture : CoreTest<ImportApprovedTracks>
    {
        private List<ImportDecision<LocalTrack>> _rejectedDecisions;
        private List<ImportDecision<LocalTrack>> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private DownloadClientItemClientInfo _clientInfo;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision<LocalTrack>>();
            _approvedDecisions = new List<ImportDecision<LocalTrack>>();

            var artist = Builder<Artist>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\Music\Alien Ant Farm".AsOsAgnostic())
                                        .Build();

            var album = Builder<Album>.CreateNew()
                .With(e => e.Artist = artist)
                .Build();

            var release = Builder<AlbumRelease>.CreateNew()
                .With(e => e.AlbumId = album.Id)
                .With(e => e.Monitored = true)
                .Build();

            album.AlbumReleases = new List<AlbumRelease> { release };

            var tracks = Builder<Track>.CreateListOfSize(5)
                                           .Build();

            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalTrack>(new LocalTrack(), new Rejection("Rejected!")));

            foreach (var track in tracks)
            {
                _approvedDecisions.Add(new ImportDecision<LocalTrack>(
                                           new LocalTrack
                                           {
                                               Artist = artist,
                                               Album = album,
                                               Release = release,
                                               Tracks = new List<Track> { track },
                                               Path = Path.Combine(artist.Path, "Alien Ant Farm - 01 - Pilot.mp3"),
                                               Quality = new QualityModel(Quality.MP3_256),
                                               FileTrackInfo = new ParsedTrackInfo
                                               {
                                                   ReleaseGroup = "DRONE"
                                               }
                                           }));
            }

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeTrackFile(It.IsAny<TrackFile>(), It.IsAny<LocalTrack>(), It.IsAny<bool>()))
                  .Returns(new TrackFileMoveResult());

            _clientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();
            _downloadClientItem = Builder<DownloadClientItem>.CreateNew().With(x => x.DownloadClientInfo = _clientInfo).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAlbum(It.IsAny<int>()))
                .Returns(new List<TrackFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesWithBasePath(It.IsAny<string>()))
                .Returns(new List<TrackFile>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetParentFolder(It.IsAny<string>()))
                .Returns<string>(p => Path.GetDirectoryName(p));
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<TrackFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(5);
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision<LocalTrack>>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_track_once()
        {
            var all = new List<ImportDecision<LocalTrack>>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision<LocalTrack>(_approvedDecisions.First().Item));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_TrackImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false),
                          Times.Never());
        }

        [Test]
        public void should_import_larger_files_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.Item.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision<LocalTrack>(
                new LocalTrack
                {
                    Artist = fileDecision.Item.Artist,
                    Album = fileDecision.Item.Album,
                    Tracks = new List<Track> { fileDecision.Item.Tracks.First() },
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.MP3_256),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision<LocalTrack>>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == fileDecision.Item.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo }, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeTrackFile(It.IsAny<TrackFile>(), _approvedDecisions.First().Item, false), Times.Once());
        }

        [Test]
        public void should_delete_existing_trackfiles_with_the_same_path()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(Builder<TrackFile>.CreateNew().Build());

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalTrack>> { track }, false);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<TrackFile>(), DeleteMediaFileReason.ManualOverride), Times.Once());
        }

        private LocalTrack GivenExistingFile(int index = 0, long size = 1000)
        {
            var localTrack = _approvedDecisions[index].Item;
            localTrack.ExistingFile = true;
            localTrack.Size = size;
            localTrack.Modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return localTrack;
        }

        private void GivenLinkedFile(LocalTrack localTrack, long size, List<Track> tracks)
        {
            var trackFile = new TrackFile
            {
                Id = 7,
                Path = localTrack.Path.CleanFilePath(),
                AlbumId = localTrack.Album.Id,
                Size = size,
                Modified = localTrack.Modified,
                Tracks = tracks
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(trackFile.Path))
                .Returns(trackFile);
        }

        [Test]
        public void should_skip_an_unchanged_file_already_linked_to_its_tracks()
        {
            var localTrack = GivenExistingFile();
            GivenLinkedFile(localTrack, localTrack.Size, localTrack.Tracks.ToList());

            var results = Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, false);

            results.Should().ContainSingle(r => r.Result == ImportResultType.Skipped);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<TrackFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.AddMany(It.Is<List<TrackFile>>(l => l.Any())), Times.Never());
            Mocker.GetMock<IAudioTagService>().Verify(v => v.WriteTags(It.IsAny<TrackFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_reimport_an_existing_file_linked_to_other_tracks()
        {
            var localTrack = GivenExistingFile();
            GivenLinkedFile(localTrack, localTrack.Size, new List<Track> { _approvedDecisions[1].Item.Tracks.First() });

            var results = Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, false);

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
        }

        [Test]
        public void should_reimport_an_existing_file_changed_on_disk()
        {
            var localTrack = GivenExistingFile();
            GivenLinkedFile(localTrack, localTrack.Size + 1, localTrack.Tracks.ToList());

            var results = Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, false);

            results.Should().ContainSingle(r => r.Result == ImportResultType.Imported);
        }

        [Test]
        public void should_not_link_another_file_to_tracks_an_unchanged_file_already_holds()
        {
            var linked = GivenExistingFile(size: 2000);
            GivenLinkedFile(linked, linked.Size, linked.Tracks.ToList());

            var duplicate = new ImportDecision<LocalTrack>(new LocalTrack
            {
                Artist = linked.Artist,
                Album = linked.Album,
                Release = linked.Release,
                Tracks = linked.Tracks.ToList(),
                Path = Path.Combine(linked.Artist.Path, "Alien Ant Farm - 01 - Pilot [copy].mp3"),
                Quality = new QualityModel(Quality.MP3_256),
                Size = 1000,
                ExistingFile = true,
                FileTrackInfo = new ParsedTrackInfo()
            });

            var results = Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First(), duplicate }, false);

            results.Should().NotContain(r => r.Result == ImportResultType.Imported);
        }

        private TrackFile GivenOrphan(string fileName, ParsedTrackInfo tags, int albumId = 0)
        {
            var localTrack = _approvedDecisions.First().Item;
            var orphan = new TrackFile
            {
                Id = 9,
                AlbumId = albumId,
                Path = Path.Combine(Path.GetDirectoryName(localTrack.Path), fileName),
                Tracks = new List<Track>()
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesWithBasePath(Path.GetDirectoryName(localTrack.Path)))
                .Returns(new List<TrackFile> { orphan });

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(orphan.Path))
                .Returns(true);

            Mocker.GetMock<IAudioTagService>()
                .Setup(s => s.ReadTags(orphan.Path))
                .Returns(tags);

            return orphan;
        }

        private ParsedTrackInfo TagsOf(Track track)
        {
            return new ParsedTrackInfo { Title = track.Title, TrackNumbers = new[] { track.AbsoluteTrackNumber }, DiscNumber = track.MediumNumber };
        }

        private void VerifyRecycled(TrackFile orphan, Times times)
        {
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(orphan.Path, It.IsAny<string>()), times);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(orphan, DeleteMediaFileReason.Upgrade), times);
        }

        [Test]
        public void should_recycle_an_unmapped_copy_of_a_track_a_download_replaces()
        {
            var orphan = GivenOrphan("Alien Ant Farm - 01 - Pilot [old].flac", TagsOf(_approvedDecisions.First().Item.Tracks.First()));

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            VerifyRecycled(orphan, Times.Once());
        }

        [Test]
        public void should_keep_an_unmapped_file_of_another_track()
        {
            var tags = TagsOf(_approvedDecisions.First().Item.Tracks.First());
            tags.Title += " (Extended Mix)";
            var orphan = GivenOrphan("Alien Ant Farm - 02 - Pilot Extended Mix.flac", tags);

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            VerifyRecycled(orphan, Times.Never());
        }

        [Test]
        public void should_keep_an_unmapped_file_of_another_recording_with_the_same_title()
        {
            var tags = TagsOf(_approvedDecisions.First().Item.Tracks.First());
            tags.RecordingMBId = "another-recording";
            var orphan = GivenOrphan("Alien Ant Farm - 01 - Pilot [live].flac", tags);

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            VerifyRecycled(orphan, Times.Never());
        }

        [Test]
        public void should_keep_an_unmapped_copy_in_a_subfolder()
        {
            var orphan = GivenOrphan(Path.Combine("Extras", "Alien Ant Farm - 01 - Pilot.flac"), TagsOf(_approvedDecisions.First().Item.Tracks.First()));

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            VerifyRecycled(orphan, Times.Never());
        }

        [Test]
        public void should_not_look_for_unmapped_copies_on_a_rescan()
        {
            var localTrack = GivenExistingFile();
            var orphan = GivenOrphan("Alien Ant Farm - 01 - Pilot [old].flac", TagsOf(localTrack.Tracks.First()));

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, false);

            VerifyRecycled(orphan, Times.Never());
        }

        [Test]
        public void should_leave_a_file_still_on_its_album_to_the_existing_cleanup()
        {
            var orphan = GivenOrphan("Alien Ant Farm - 01 - Pilot [old].flac", TagsOf(_approvedDecisions.First().Item.Tracks.First()), albumId: 5);

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(orphan.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_include_scene_name_with_new_downloads()
        {
            var firstDecision = _approvedDecisions.First();
            firstDecision.Item.SceneName = "Artist.Name.Album.Name.TrackNum.Track.Title.MP3256";

            Subject.Import(new List<ImportDecision<LocalTrack>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(v => v.UpgradeTrackFile(It.Is<TrackFile>(e => e.SceneName == firstDecision.Item.SceneName), _approvedDecisions.First().Item, false),
                    Times.Once());
        }
    }
}
