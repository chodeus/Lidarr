using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Specifications
{
    // The real format calculation and title specifications; only the format list and history are stubbed
    [TestFixture]
    public class MoreTracksSpecificationRealFormatsFixture : CoreTest<MoreTracksSpecification>
    {
        private const string GrabTitle = "Artist - Album (2020) [WEB FLAC]";

        private Artist _artist;
        private AlbumRelease _existingRelease;
        private AlbumRelease _smallerRelease;
        private List<EntityHistory> _imports;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IUpgradableSpecification>(Mocker.Resolve<UpgradableSpecification>());
            Mocker.SetConstant<ICustomFormatCalculationService>(Mocker.Resolve<CustomFormatCalculationService>());

            var web = new CustomFormat("WEB", new ReleaseTitleSpecification { Value = "WEB" }) { Id = 1 };
            var cd = new CustomFormat("CD", new ReleaseTitleSpecification { Value = "\\bCD\\b" }) { Id = 2 };

            Mocker.GetMock<ICustomFormatService>()
                  .Setup(s => s.All())
                  .Returns(new List<CustomFormat> { web, cd });

            _artist = new Artist
            {
                Metadata = new ArtistMetadata { Name = "Artist" },
                QualityProfile = new QualityProfile
                {
                    Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3_320, Quality.FLAC, Quality.FLAC_24),
                    Cutoff = Quality.FLAC_24.Id,
                    CutoffFormatScore = 100,
                    FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = web, Score = 50 }, new ProfileFormatItem { Format = cd, Score = 3 } },
                    UpgradeAllowed = true,
                    AllowSmallerReleaseUpgrades = true
                }
            };

            var album = new Album { Id = 1 };
            _existingRelease = new AlbumRelease { Id = 10, Monitored = true, Album = album };
            _smallerRelease = new AlbumRelease { Id = 11, Monitored = false, Album = album };
            album.AlbumReleases = new List<AlbumRelease> { _existingRelease, _smallerRelease };

            _imports = new List<EntityHistory>();
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByAlbum(album.Id, EntityHistoryEventType.TrackFileImported))
                  .Returns(() => _imports);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Find("dl-1", EntityHistoryEventType.Grabbed))
                  .Returns(new List<EntityHistory>
                  {
                      new EntityHistory
                      {
                          EventType = EntityHistoryEventType.Grabbed,
                          SourceTitle = GrabTitle,
                          Quality = new QualityModel(Quality.FLAC),
                          Date = DateTime.UtcNow.AddHours(-1)
                      }
                  });
        }

        private void GivenExistingFilesGrabbedAs(string title)
        {
            _imports.AddRange(Enumerable.Range(1, 3).Select(i => new EntityHistory { EventType = EntityHistoryEventType.TrackFileImported, TrackId = i, DownloadId = "dl-0", Date = DateTime.UtcNow.AddDays(-10) }));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Find("dl-0", EntityHistoryEventType.Grabbed))
                  .Returns(new List<EntityHistory> { new EntityHistory { EventType = EntityHistoryEventType.Grabbed, SourceTitle = title, Quality = new QualityModel(Quality.FLAC), Date = DateTime.UtcNow.AddDays(-11) } });
        }

        // Mirrors the TrackFile ImportApprovedTracks writes: SceneName comes from LocalTrack.SceneName, null unless SceneSource.
        private void GivenExistingFilesImportedFrom(string sceneName)
        {
            _existingRelease.Tracks = Enumerable.Range(1, 3).Select(i => new Track
            {
                Id = i,
                TrackFileId = i,
                TrackFile = new TrackFile
                {
                    Id = i,
                    Quality = new QualityModel(Quality.FLAC),
                    SceneName = sceneName,
                    OriginalFilePath = $"{GrabTitle}/0{i} - Song.flac",
                    Path = $"/music/Artist/Album/0{i} - Song.flac",
                    Size = 30_000_000
                }
            }).ToList();
        }

        private LocalAlbumRelease IncomingFlacSingle()
        {
            return new LocalAlbumRelease(new List<LocalTrack>
            {
                new LocalTrack
                {
                    Path = "/downloads/" + GrabTitle + "/01 - Song.flac",
                    Quality = new QualityModel(Quality.FLAC),
                    Artist = _artist,
                    Size = 30_000_000
                }
            })
            {
                AlbumRelease = _smallerRelease
            };
        }

        [Test]
        public void should_reject_a_smaller_release_with_the_same_quality_and_the_same_grab_title_as_production_files()
        {
            GivenExistingFilesImportedFrom(null);

            Subject.IsSatisfiedBy(IncomingFlacSingle(), new DownloadClientItem { DownloadId = "dl-1" })
                   .Accepted.Should().BeFalse("same quality, same release title, same custom formats: not an upgrade");
        }

        [Test]
        public void should_reject_when_existing_files_carry_the_scene_name()
        {
            GivenExistingFilesImportedFrom(GrabTitle);

            Subject.IsSatisfiedBy(IncomingFlacSingle(), new DownloadClientItem { DownloadId = "dl-1" })
                   .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_smaller_release_grabbed_with_the_same_title_as_the_files_on_disk()
        {
            GivenExistingFilesImportedFrom(null);
            GivenExistingFilesGrabbedAs(GrabTitle);

            Subject.IsSatisfiedBy(IncomingFlacSingle(), new DownloadClientItem { DownloadId = "dl-1" })
                   .Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_smaller_release_whose_grab_beats_the_grab_of_the_files_on_disk()
        {
            GivenExistingFilesImportedFrom(null);
            GivenExistingFilesGrabbedAs("Artist - Album (2019) [CD FLAC]");

            Subject.IsSatisfiedBy(IncomingFlacSingle(), new DownloadClientItem { DownloadId = "dl-1" })
                   .Accepted.Should().BeTrue();
        }
    }
}
