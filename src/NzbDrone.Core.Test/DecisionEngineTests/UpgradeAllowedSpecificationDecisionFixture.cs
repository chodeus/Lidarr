using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class UpgradeAllowedSpecificationDecisionFixture : CoreTest<UpgradeAllowedSpecification>
    {
        private RemoteAlbum _remoteAlbum;
        private CustomFormat _formatTen;
        private TrackFile _firstFile;
        private TrackFile _secondFile;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();

            _formatTen = new CustomFormat { Id = 1, Name = "Ten" };
            _firstFile = new TrackFile { Quality = new QualityModel(Quality.FLAC) };
            _secondFile = new TrackFile { Quality = new QualityModel(Quality.FLAC) };

            var profile = new QualityProfile
            {
                UpgradeAllowed = false,
                Cutoff = Quality.FLAC.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = _formatTen, Score = 10 } }
            };

            _remoteAlbum = new RemoteAlbum
            {
                Artist = Builder<Artist>.CreateNew().With(c => c.QualityProfile = profile).Build(),
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC) },
                Albums = new List<Album> { new Album() },
                CustomFormats = new List<CustomFormat> { _formatTen }
            };

            Mocker.GetMock<ITrackService>()
                  .Setup(c => c.TracksWithoutFiles(It.IsAny<int>()))
                  .Returns(new List<Track>());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.GetFilesByAlbum(It.IsAny<int>()))
                  .Returns(new List<TrackFile> { _firstFile, _secondFile });

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(It.IsAny<TrackFile>(), It.IsAny<Artist>()))
                  .Returns(new List<CustomFormat>());

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(_firstFile, It.IsAny<Artist>()))
                  .Returns(new List<CustomFormat> { _formatTen });

            // The album-level call reads each file's stubbed formats
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormats(It.IsAny<List<TrackFile>>(), It.IsAny<Artist>()))
                  .Returns<List<TrackFile>, Artist>((files, artist) => files.Select(f => Mocker.Resolve<ICustomFormatCalculationService>().ParseCustomFormat(f, artist)).ToList());
        }

        [Test]
        public void should_reject_when_a_later_file_would_gain_custom_format_score_with_upgrades_off()
        {
            var decision = Subject.IsSatisfiedBy(_remoteAlbum, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Existing files and the Quality profile does not allow upgrades");
        }

        [Test]
        public void should_accept_when_no_file_would_gain_custom_format_score_with_upgrades_off()
        {
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(_secondFile, It.IsAny<Artist>()))
                  .Returns(new List<CustomFormat> { _formatTen });

            Subject.IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeTrue();
        }
    }
}
