using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class AlbumArtistCreditRefreshFixture : DbTest<AlbumArtistCreditRepository, AlbumArtistCredit>
    {
        private AlbumRepository _albums;
        private ArtistMetadata _primary;
        private ArtistMetadata _guest;
        private NamingConfig _naming;
        private AlbumArtistCreditService _service;

        [SetUp]
        public void Setup()
        {
            _albums = Mocker.Resolve<AlbumRepository>();
            var metadata = Mocker.Resolve<ArtistMetadataRepository>();
            _primary = metadata.Insert(new ArtistMetadata { ForeignArtistId = "aaaaaaaa-0000-0000-0000-000000000001", Name = "Primary" });
            _guest = metadata.Insert(new ArtistMetadata { ForeignArtistId = "aaaaaaaa-0000-0000-0000-000000000002", Name = "Guest" });

            _naming = NamingConfig.Default;
            _naming.RenameTracks = true;
            _naming.StandardTrackFormat = "{Album Title}{ (Album Guests)}";
            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(() => _naming);

            Mocker.SetConstant<IAlbumArtistCreditRepository>(Subject);
            _service = Mocker.Resolve<AlbumArtistCreditService>();
            _service.Handle(new ApplicationStartedEvent());
        }

        [TearDown]
        public void ResetLookup()
        {
            AlbumArtistCreditLookup.Reset();
        }

        private Album GivenAlbum(string title, int addedYear, ArtistMetadata artist = null)
        {
            return _albums.Insert(new Album
            {
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                ForeignAlbumId = Guid.NewGuid().ToString(),
                ArtistMetadataId = (artist ?? _primary).Id,
                AlbumType = "Single",
                Added = new DateTime(addedYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        private void GivenFile(Album album)
        {
            Db.Insert(Builder<TrackFile>.CreateNew().With(f => f.Id = 0).With(f => f.AlbumId = album.Id).With(f => f.Path = $"/music/{album.Id}.flac").With(f => f.Quality = new QualityModel(Quality.FLAC)).Build());
        }

        private void GivenCredit(Func<string, List<AlbumArtistCreditName>> credit)
        {
            Mocker.GetMock<IMusicBrainzArtistCreditProxy>().Setup(p => p.GetCredit(It.IsAny<string>())).Returns(credit);
        }

        private List<AlbumArtistCreditName> PrimaryAndGuest()
        {
            return new List<AlbumArtistCreditName>
            {
                new AlbumArtistCreditName { Name = "Primary", ForeignArtistId = _primary.ForeignArtistId, JoinPhrase = " & " },
                new AlbumArtistCreditName { Name = "Guest", ForeignArtistId = _guest.ForeignArtistId, JoinPhrase = "" }
            };
        }

        private List<AlbumArtistCreditName> PrimaryOnly()
        {
            return new List<AlbumArtistCreditName> { new AlbumArtistCreditName { Name = "Primary", ForeignArtistId = _primary.ForeignArtistId, JoinPhrase = "" } };
        }

        private string FolderName(Album album)
        {
            Mocker.GetMock<IQualityDefinitionService>()
                  .Setup(v => v.Get(It.IsAny<Quality>()))
                  .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
            Mocker.GetMock<ICustomFormatService>().Setup(v => v.All()).Returns(new List<CustomFormat>());

            var artist = new Artist { Name = "Primary", Metadata = _primary };
            var track = new Track { Title = "Song", AlbumRelease = new AlbumRelease { Media = new List<Medium> { new Medium { Number = 1 } } }, MediumNumber = 1, ArtistMetadata = _primary };
            var file = new TrackFile { Quality = new QualityModel(Quality.FLAC) };

            return Mocker.Resolve<FileNameBuilder>().BuildTrackFileName(new List<Track> { track }, artist, album, file);
        }

        [Test]
        public void should_not_let_albums_that_always_fail_starve_the_rest_of_the_queue()
        {
            var failing = Enumerable.Range(0, 5).Select(i => GivenAlbum($"Failing {i}", 2025)).ToList();
            var healthy = GivenAlbum("Healthy", 2010);
            var failingIds = failing.Select(a => a.ForeignAlbumId).ToHashSet();
            GivenCredit(id => failingIds.Contains(id) ? null : PrimaryAndGuest());

            _service.Execute(new RefreshAlbumArtistCreditsCommand());
            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            Subject.FindByAlbumId(healthy.Id).Should().NotBeNull();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_neither_return_nor_keep_the_credit_of_an_album_deleted_while_it_was_fetched()
        {
            var album = GivenAlbum("Song", 2020);
            GivenCredit(_ =>
            {
                _albums.Delete(album.Id);
                _service.Handle(new AlbumDeletedEvent(album, false, false));
                return PrimaryAndGuest();
            });

            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            Subject.FindByAlbumId(album.Id).Should().BeNull();

            Mocker.Resolve<CleanupOrphanedAlbumArtistCredits>().Clean();
            Subject.All().Should().NotContain(c => c.AlbumId == album.Id);
        }

        [Test]
        public void should_name_guests_against_the_albums_current_artist()
        {
            var album = GivenAlbum("Song", 2020);
            GivenCredit(_ => PrimaryAndGuest());
            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            album.ArtistMetadataId = _guest.Id;
            _albums.Update(album);

            AlbumArtistCreditLookup.Guests(album).Should().Be("Primary");
        }

        [Test]
        public void should_fetch_the_credits_of_a_new_album_and_its_same_titled_sibling_right_away()
        {
            var solo = GivenAlbum("Song", 2020);
            Subject.Insert(new AlbumArtistCredit { AlbumId = solo.Id, Credit = "Primary", Guests = "", Artists = PrimaryOnly(), LastFetched = DateTime.UtcNow });
            var featuring = GivenAlbum("Song", 2021);
            GivenAlbum("Unrelated", 2022);
            GivenCredit(_ => PrimaryAndGuest());

            RefreshAlbumArtistCreditsCommand pushed = null;
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<RefreshAlbumArtistCreditsCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<RefreshAlbumArtistCreditsCommand, CommandPriority, CommandTrigger>((c, _, _) => pushed = c);

            _service.Handle(new AlbumAddedEvent(featuring));
            pushed.AlbumIds.Should().BeEquivalentTo(new[] { featuring.Id, solo.Id });

            _service.Execute(pushed);

            Mocker.GetMock<IMusicBrainzArtistCreditProxy>().Verify(p => p.GetCredit(It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMusicBrainzArtistCreditProxy>().Verify(p => p.GetCredit(featuring.ForeignAlbumId), Times.Once());
            FolderName(featuring).Should().NotBe(FolderName(solo));
        }

        [Test]
        public void should_not_fetch_right_away_for_an_album_without_a_same_titled_sibling()
        {
            var album = GivenAlbum("Song", 2020);
            GivenAlbum("Other Song", 2020);

            _service.Handle(new AlbumAddedEvent(album));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.IsAny<RefreshAlbumArtistCreditsCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_warn_when_the_guests_of_an_album_with_files_change()
        {
            var album = GivenAlbum("Song", 2020);
            GivenFile(album);
            GivenCredit(_ => PrimaryAndGuest());

            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_warn_when_the_naming_leaves_out_guests()
        {
            _naming.StandardTrackFormat = "{Album Title}";
            var album = GivenAlbum("Song", 2020);
            GivenFile(album);
            GivenCredit(_ => PrimaryAndGuest());

            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            ExceptionVerification.ExpectedWarns(0);
        }

        [Test]
        public void should_not_warn_for_an_album_without_files_or_without_guests()
        {
            var withoutFiles = GivenAlbum("Song", 2020);
            var withoutGuests = GivenAlbum("Other Song", 2020);
            GivenFile(withoutGuests);
            GivenCredit(id => id == withoutFiles.ForeignAlbumId ? PrimaryAndGuest() : PrimaryOnly());

            _service.Execute(new RefreshAlbumArtistCreditsCommand());

            ExceptionVerification.ExpectedWarns(0);
        }
    }
}
