using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Music;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Plex
{
    public class PlexServerServiceFixture : CoreTest<PlexServerService>
    {
        private const string RootFolderPath = @"C:\Test\Music";

        private Artist _artist;
        private PlexServerSettings _settings;

        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            _artist = new Artist
            {
                Path = @"C:\Test\Music\Artist Name".AsOsAgnostic()
            };

            _settings = new PlexServerSettings
            {
                Host = "127.0.0.1",
                Port = 32400
            };

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>()))
                .Returns(RootFolderPath.AsOsAgnostic());

            Mocker.GetMock<IPlexServerProxy>()
                .Setup(s => s.Version(_settings))
                .Returns("1.20.0.12345-abcdef");
        }

        private static PlexSection GivenSection(int id, params string[] paths)
        {
            var section = new PlexSection
            {
                Id = id
            };

            foreach (var path in paths)
            {
                section.Locations.Add(new PlexSectionLocation
                {
                    Id = section.Locations.Count + 1,
                    Path = path.AsOsAgnostic()
                });
            }

            return section;
        }

        private void GivenSections(params PlexSection[] sections)
        {
            Mocker.GetMock<IPlexServerProxy>()
                .Setup(s => s.GetArtistSections(_settings))
                .Returns(new List<PlexSection>(sections));
        }

        private void VerifyUpdated(int sectionId, Times times)
        {
            Mocker.GetMock<IPlexServerProxy>()
                .Verify(v => v.Update(sectionId, It.IsAny<string>(), _settings), times);
        }

        [Test]
        public void should_update_all_sections_matching_the_artist_path()
        {
            GivenSections(GivenSection(1, RootFolderPath), GivenSection(2, RootFolderPath));

            Subject.UpdateLibrary(_artist, _settings);

            VerifyUpdated(1, Times.Once());
            VerifyUpdated(2, Times.Once());
        }

        [Test]
        public void should_update_only_the_matching_section_when_a_single_section_matches()
        {
            GivenSections(GivenSection(1, RootFolderPath), GivenSection(2, @"C:\Test\OtherMusic"));

            Subject.UpdateLibrary(_artist, _settings);

            VerifyUpdated(1, Times.Once());
            VerifyUpdated(2, Times.Never());
        }

        [Test]
        public void should_only_update_a_section_once_when_multiple_locations_match()
        {
            GivenSections(GivenSection(1, RootFolderPath, RootFolderPath));

            Subject.UpdateLibrary(_artist, _settings);

            VerifyUpdated(1, Times.Once());
        }

        [Test]
        public void should_fall_back_to_updating_every_section_when_none_match()
        {
            GivenSections(GivenSection(1, @"C:\Test\OtherMusic"), GivenSection(2, @"C:\Test\OtherMusic2"));

            Subject.UpdateLibrary(_artist, _settings);

            VerifyUpdated(1, Times.Once());
            VerifyUpdated(2, Times.Once());
        }
    }
}
