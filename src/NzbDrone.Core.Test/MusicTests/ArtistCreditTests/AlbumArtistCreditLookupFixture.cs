using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class AlbumArtistCreditLookupFixture : CoreTest
    {
        private readonly Album _album = new Album { Id = 5 };
        private readonly ArtistMetadata _primary = new ArtistMetadata { Id = 10, Name = "Artist Name" };
        private readonly ArtistMetadata _guest = new ArtistMetadata { Id = 11, Name = "Guest Name" };

        private void GivenCredit(bool writeToTags)
        {
            var credit = new AlbumArtistCredit { AlbumId = 5, Credit = "Artist Name, Guest Name & Other", Guests = "Guest Name & Other" };
            AlbumArtistCreditLookup.Configure(id => id == 5 ? credit : null, () => writeToTags);
        }

        [TearDown]
        public void TearDown()
        {
            AlbumArtistCreditLookup.Reset();
        }

        [Test]
        public void should_return_no_guests_until_configured()
        {
            AlbumArtistCreditLookup.Guests(_album).Should().BeEmpty();
            AlbumArtistCreditLookup.AlbumArtistTag(_album, "Artist Name").Should().Be("Artist Name");
        }

        [Test]
        public void should_return_the_stored_guests()
        {
            GivenCredit(false);

            AlbumArtistCreditLookup.Guests(_album).Should().Be("Guest Name & Other");
        }

        [Test]
        public void should_return_no_guests_for_an_unsaved_album()
        {
            GivenCredit(false);

            AlbumArtistCreditLookup.Guests(new Album()).Should().BeEmpty();
        }

        [Test]
        public void should_keep_the_primary_name_when_the_option_is_off()
        {
            GivenCredit(false);

            AlbumArtistCreditLookup.AlbumArtistTag(_album, "Artist Name").Should().Be("Artist Name");
        }

        [Test]
        public void should_write_the_full_credit_when_the_option_is_on()
        {
            GivenCredit(true);

            AlbumArtistCreditLookup.AlbumArtistTag(_album, "Artist Name").Should().Be("Artist Name, Guest Name & Other");
            AlbumArtistCreditLookup.TrackArtistTag(_album, _primary, _primary.Id).Should().Be("Artist Name, Guest Name & Other");
        }

        [Test]
        public void should_keep_the_primary_name_without_a_stored_credit()
        {
            GivenCredit(true);

            AlbumArtistCreditLookup.AlbumArtistTag(new Album { Id = 6 }, "Artist Name").Should().Be("Artist Name");
        }

        [Test]
        public void should_keep_the_artist_of_a_track_by_someone_else()
        {
            GivenCredit(true);

            AlbumArtistCreditLookup.TrackArtistTag(_album, _guest, _primary.Id).Should().Be("Guest Name");
        }
    }
}
