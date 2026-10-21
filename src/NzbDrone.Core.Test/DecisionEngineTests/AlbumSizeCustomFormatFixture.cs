using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
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
    // The real custom format calculation and upgrade rules; the format list, files and config are mocked
    [TestFixture]
    public class AlbumSizeCustomFormatFixture : CoreTest
    {
        private const string Title = "Artist - Album (2020) [FLAC]";

        private CustomFormat _sizeFormat;
        private RemoteAlbum _remoteAlbum;

        [SetUp]
        public void Setup()
        {
            _sizeFormat = new CustomFormat("Album 0.3-3 GB", new SizeSpecification { Min = 0.3, Max = 3 }) { Id = 1 };

            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat> { _sizeFormat });
            Mocker.GetMock<IConfigService>().SetupGet(s => s.DownloadPropersAndRepacks).Returns(ProperDownloadTypes.PreferAndUpgrade);
            Mocker.SetConstant<ICustomFormatCalculationService>(Mocker.Resolve<CustomFormatCalculationService>());
            Mocker.SetConstant<IUpgradableSpecification>(Mocker.Resolve<UpgradableSpecification>());

            var profile = new QualityProfile
            {
                Name = "Size",
                UpgradeAllowed = true,
                Cutoff = Quality.FLAC.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                CutoffFormatScore = 50,
                MinUpgradeFormatScore = 1,
                FormatItems = new List<ProfileFormatItem> { new ProfileFormatItem { Format = _sizeFormat, Score = 50 } }
            };

            var artist = Builder<Artist>.CreateNew().With(a => a.QualityProfile = profile).Build();

            // The grabbed release on disk: ten 40 MB tracks of a 400 MB release
            var files = Enumerable.Range(1, 10)
                                  .Select(i => new TrackFile { Quality = new QualityModel(Quality.FLAC), SceneName = Title, Size = 40L * 1024 * 1024, Path = $"/music/Artist/Album/{i:00}.flac" })
                                  .ToList();

            Mocker.GetMock<ITrackService>().Setup(c => c.TracksWithoutFiles(It.IsAny<int>())).Returns(new List<Track>());
            Mocker.GetMock<IMediaFileService>().Setup(c => c.GetFilesByAlbum(It.IsAny<int>())).Returns(files);

            _remoteAlbum = new RemoteAlbum
            {
                Artist = artist,
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel(Quality.FLAC), ReleaseTitle = Title },
                Albums = new List<Album> { new Album { Id = 1 } },
                Release = new ReleaseInfo { Title = Title, Size = 400L * 1024 * 1024 }
            };
            _remoteAlbum.CustomFormats = Mocker.Resolve<ICustomFormatCalculationService>().ParseCustomFormat(_remoteAlbum, _remoteAlbum.Release.Size);
        }

        [Test]
        public void should_not_take_the_release_already_on_disk_again()
        {
            _remoteAlbum.CustomFormats.Should().Contain(_sizeFormat);

            Mocker.Resolve<UpgradeDiskSpecification>().IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_count_a_size_format_toward_the_cutoff_by_the_albums_size()
        {
            Mocker.Resolve<CutoffSpecification>().IsSatisfiedBy(_remoteAlbum, null).Accepted.Should().BeFalse();
        }
    }
}
