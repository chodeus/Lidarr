using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class CutoffSpecificationDecisionFixture : CoreTest<CutoffSpecification>
    {
        private RemoteAlbum _remoteAlbum;
        private QualityProfile _profile;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();

            _profile = new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.MP3_320.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities()
            };

            _remoteAlbum = new RemoteAlbum
            {
                Artist = Builder<Artist>.CreateNew().With(c => c.QualityProfile = _profile).Build(),
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC) },
                Albums = new List<Album> { new Album() },
                CustomFormats = new List<CustomFormat>()
            };

            Mocker.GetMock<ITrackService>()
                  .Setup(c => c.TracksWithoutFiles(It.IsAny<int>()))
                  .Returns(new List<Track>());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.GetFilesByAlbum(It.IsAny<int>()))
                  .Returns(new List<TrackFile> { new TrackFile { Quality = new QualityModel(Quality.MP3_320) } });

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(It.IsAny<TrackFile>()))
                  .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_reject_when_existing_files_meet_both_cutoffs()
        {
            var decision = Subject.IsSatisfiedBy(_remoteAlbum, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Existing files meets cutoff: " + _profile.Items[_profile.GetIndex(Quality.MP3_320.Id).Index]);
        }

        [Test]
        public void should_name_the_first_allowed_quality_as_the_cutoff_when_upgrades_are_off()
        {
            _profile.UpgradeAllowed = false;
            var firstAllowed = _profile.Items[_profile.GetIndex(_profile.FirstAllowedQuality().Id).Index];

            var decision = Subject.IsSatisfiedBy(_remoteAlbum, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Existing files meets cutoff: " + firstAllowed);
        }
    }
}
