using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
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
    [TestFixture]
    public class MoreTracksSpecificationFixture : CoreTest<MoreTracksSpecification>
    {
        private Artist _artist;
        private Album _album;
        private AlbumRelease _existingRelease;
        private AlbumRelease _singleRelease;
        private List<CustomFormat> _formats;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IUpgradableSpecification>(Mocker.Resolve<UpgradableSpecification>());

            _formats = new List<CustomFormat>
            {
                new CustomFormat("WEB") { Id = 1 },
                new CustomFormat("CD") { Id = 2 },
                new CustomFormat("Vinyl") { Id = 3 }
            };

            _artist = new Artist
            {
                QualityProfile = new QualityProfile
                {
                    Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3_256, Quality.MP3_320, Quality.FLAC, Quality.FLAC_24),
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem { Format = _formats[0], Score = 50 },
                        new ProfileFormatItem { Format = _formats[1], Score = 3 },
                        new ProfileFormatItem { Format = _formats[2], Score = -10 }
                    },
                    AllowSmallerReleaseUpgrades = true
                }
            };

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<LocalTrack>()))
                  .Returns<LocalTrack>(t => FormatsNamed(t.SceneName));

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<TrackFile>(), It.IsAny<Artist>()))
                  .Returns<TrackFile, Artist>((f, a) => FormatsNamed(f.SceneName));

            _album = new Album { Id = 1 };
            _existingRelease = new AlbumRelease { Id = 10, Monitored = true, Album = _album };
            _singleRelease = new AlbumRelease { Id = 11, Monitored = false, Album = _album };
            _album.AlbumReleases = new List<AlbumRelease> { _existingRelease, _singleRelease };

            GivenExistingFiles(Quality.MP3_320, Quality.MP3_320, Quality.MP3_320);
        }

        private List<CustomFormat> FormatsNamed(string name)
        {
            return _formats.Where(x => x.Name == name).ToList();
        }

        private void GivenExistingFiles(params Quality[] qualities)
        {
            GivenExistingFiles(null, qualities);
        }

        private void GivenExistingFiles(string format, params Quality[] qualities)
        {
            _existingRelease.Tracks = qualities.Select((q, i) => new Track
            {
                Id = i + 1,
                TrackFileId = i + 1,
                TrackFile = new TrackFile { Id = i + 1, Quality = new QualityModel(q), SceneName = format }
            }).ToList();
        }

        private LocalAlbumRelease Incoming(AlbumRelease release, params Quality[] qualities)
        {
            return Incoming(release, null, qualities);
        }

        private LocalAlbumRelease Incoming(AlbumRelease release, string format, params Quality[] qualities)
        {
            return new LocalAlbumRelease(qualities.Select((q, i) => new LocalTrack
            {
                Path = $"/downloads/album/{i + 1}.flac",
                Quality = new QualityModel(q),
                SceneName = format,
                Artist = _artist
            }).ToList())
            {
                AlbumRelease = release
            };
        }

        [Test]
        public void should_accept_a_smaller_release_that_upgrades_every_file()
        {
            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.FLAC), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_smaller_release_upgrade_when_the_profile_does_not_allow_it()
        {
            _artist.QualityProfile.Value.AllowSmallerReleaseUpgrades = false;

            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.FLAC), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_smaller_release_at_the_same_quality()
        {
            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.MP3_320), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_smaller_release_at_a_lower_quality()
        {
            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.MP3_256), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_smaller_release_when_one_incoming_file_is_no_upgrade()
        {
            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.FLAC, Quality.MP3_320), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_smaller_release_replacing_a_mixed_album()
        {
            GivenExistingFiles(Quality.FLAC, Quality.MP3_320, Quality.MP3_320);

            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.FLAC), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_smaller_release_that_would_downgrade_a_file()
        {
            GivenExistingFiles(Quality.FLAC_24, Quality.MP3_320, Quality.MP3_320);

            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.FLAC), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_smaller_release_at_the_same_quality_with_a_better_custom_format_score()
        {
            GivenExistingFiles("CD", Quality.FLAC, Quality.FLAC, Quality.FLAC);

            Subject.IsSatisfiedBy(Incoming(_singleRelease, "WEB", Quality.FLAC), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_smaller_release_at_the_same_quality_with_a_worse_custom_format_score()
        {
            GivenExistingFiles("WEB", Quality.FLAC, Quality.FLAC, Quality.FLAC);

            Subject.IsSatisfiedBy(Incoming(_singleRelease, "Vinyl", Quality.FLAC), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_smaller_release_at_a_better_quality_despite_a_worse_custom_format_score()
        {
            GivenExistingFiles("WEB", Quality.FLAC, Quality.FLAC, Quality.FLAC);

            Subject.IsSatisfiedBy(Incoming(_singleRelease, "Vinyl", Quality.FLAC_24), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_not_count_existing_library_files_as_an_upgrade()
        {
            var item = Incoming(_singleRelease, Quality.FLAC);
            item.ExistingTracks = item.LocalTracks.ToList();

            Subject.IsSatisfiedBy(item, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_fewer_files_for_the_release_already_on_disk()
        {
            Subject.IsSatisfiedBy(Incoming(_existingRelease, Quality.MP3_320), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_release_with_as_many_tracks()
        {
            Subject.IsSatisfiedBy(Incoming(_singleRelease, Quality.MP3_320, Quality.MP3_320, Quality.MP3_320), null).Accepted.Should().BeTrue();
        }
    }
}
