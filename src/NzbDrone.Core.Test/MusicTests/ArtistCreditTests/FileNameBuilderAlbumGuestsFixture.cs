using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class FileNameBuilderAlbumGuestsFixture : CoreTest<FileNameBuilder>
    {
        private Artist _artist;
        private Album _album;
        private Track _track;
        private TrackFile _trackFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<Artist>.CreateNew()
                .With(a => a.Name = "Artist Name")
                .With(a => a.Metadata = new ArtistMetadata { Name = "Artist Name" })
                .Build();

            var medium = Builder<Medium>.CreateNew().With(m => m.Number = 1).Build();
            var release = Builder<AlbumRelease>.CreateNew().With(r => r.Media = new List<Medium> { medium }).Build();

            _album = Builder<Album>.CreateNew()
                .With(a => a.Id = 5)
                .With(a => a.Title = "Album Title")
                .With(a => a.AlbumType = "Single")
                .Build();

            _track = Builder<Track>.CreateNew()
                .With(t => t.Title = "Track Title")
                .With(t => t.AlbumRelease = release)
                .With(t => t.MediumNumber = 1)
                .With(t => t.ArtistMetadata = _artist.Metadata)
                .Build();

            _trackFile = Builder<TrackFile>.CreateNew().With(f => f.Quality = new QualityModel(Quality.FLAC)).Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameTracks = true;
            _namingConfig.StandardTrackFormat = "{Album Title}{ (Album Guests)}";

            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(_namingConfig);
            Mocker.GetMock<IQualityDefinitionService>()
                  .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                  .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
            Mocker.GetMock<ICustomFormatService>().Setup(v => v.All()).Returns(new List<CustomFormat>());
        }

        [TearDown]
        public void TearDown()
        {
            AlbumArtistCreditLookup.Reset();
        }

        [Test]
        public void should_name_the_albums_guests()
        {
            AlbumArtistCreditLookup.Configure(id => new AlbumArtistCredit { AlbumId = id, Guests = "Guest One & Guest Two" }, () => false);

            Subject.BuildTrackFileName(new List<Track> { _track }, _artist, _album, _trackFile)
                   .Should().Be("Album Title (Guest One & Guest Two)");
        }

        [Test]
        public void should_drop_the_token_and_its_brackets_without_guests()
        {
            Subject.BuildTrackFileName(new List<Track> { _track }, _artist, _album, _trackFile)
                   .Should().Be("Album Title");
        }
    }
}
