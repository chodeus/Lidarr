using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Lidarr.Api.V1.TrackFiles;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class TrackFileResourceFixture : TestBase
    {
        private static TrackFileResource Map(string artistPath, string filePath)
        {
            var artist = new Artist { Id = 1, Path = artistPath, QualityProfile = new QualityProfile() };
            var file = new TrackFile { Path = filePath, Quality = new QualityModel(Quality.FLAC) };
            var formats = Mock.Of<ICustomFormatCalculationService>(s => s.ParseCustomFormat(It.IsAny<TrackFile>(), It.IsAny<Artist>()) == new List<CustomFormat>());

            return file.ToResource(artist, Mock.Of<IUpgradableSpecification>(), formats);
        }

        [Test]
        public void should_give_the_path_relative_to_the_artist_folder()
        {
            Map("/music/Artist".AsOsAgnostic(), "/music/Artist/Album/01 - Track.flac".AsOsAgnostic())
                .RelativePath.Should().Be(Path.Combine("Album", "01 - Track.flac"));
        }

        [Test]
        public void should_give_no_relative_path_for_a_file_outside_the_artist_folder()
        {
            Map("/music/Artist".AsOsAgnostic(), "/music/Artist Two/01 - Track.flac".AsOsAgnostic())
                .RelativePath.Should().BeNull();
        }
    }
}
