using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music.ArtistCredits;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistCreditTests
{
    [TestFixture]
    public class AlbumArtistCreditFixture : CoreTest
    {
        private static AlbumArtistCreditName Name(string name, string id, string join = "")
        {
            return new AlbumArtistCreditName { Name = name, ForeignArtistId = id, JoinPhrase = join };
        }

        [Test]
        public void should_join_the_credit_and_list_guests_after_the_primary_artist()
        {
            var credit = AlbumArtistCredit.For(1, new List<AlbumArtistCreditName> { Name("Artist Name", "primary", ", "), Name("Guest One", "g1", " & "), Name("Guest Two", "g2") }, "primary", DateTime.UtcNow);

            credit.Credit.Should().Be("Artist Name, Guest One & Guest Two");
            credit.Guests.Should().Be("Guest One & Guest Two");
        }

        [Test]
        public void should_list_featured_guests()
        {
            var credit = AlbumArtistCredit.For(1, new List<AlbumArtistCreditName> { Name("Artist Name", "primary", " feat. "), Name("Guest One", "g1", ", "), Name("Guest Two", "g2", " & "), Name("Guest Three", "g3") }, "primary", DateTime.UtcNow);

            credit.Credit.Should().Be("Artist Name feat. Guest One, Guest Two & Guest Three");
            credit.Guests.Should().Be("Guest One, Guest Two & Guest Three");
        }

        [Test]
        public void should_drop_a_trailing_join_phrase_when_the_primary_artist_comes_last()
        {
            var credit = AlbumArtistCredit.For(1, new List<AlbumArtistCreditName> { Name("Guest One", "g1", " & "), Name("Artist Name", "primary") }, "primary", DateTime.UtcNow);

            credit.Guests.Should().Be("Guest One");
        }

        [Test]
        public void should_name_no_guests_when_the_album_artist_is_not_credited()
        {
            var credit = AlbumArtistCredit.For(1, new List<AlbumArtistCreditName> { Name("Member One", "m1", " & "), Name("Member Two", "m2") }, "duo", DateTime.UtcNow);

            credit.Credit.Should().Be("Member One & Member Two");
            credit.Guests.Should().BeEmpty();
        }

        [Test]
        public void should_have_no_guests_for_a_solo_credit()
        {
            var credit = AlbumArtistCredit.For(1, new List<AlbumArtistCreditName> { Name("Artist Name", "primary") }, "primary", DateTime.UtcNow);

            credit.Credit.Should().Be("Artist Name");
            credit.Guests.Should().BeEmpty();
        }
    }
}
