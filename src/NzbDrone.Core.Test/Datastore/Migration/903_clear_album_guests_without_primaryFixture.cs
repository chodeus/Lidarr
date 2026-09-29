using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class clear_album_guests_without_primaryFixture : MigrationTest<clear_album_guests_without_primary>
    {
        [Test]
        public void should_clear_guests_only_where_they_repeat_the_whole_credit()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("AlbumArtistCredits").Row(new { AlbumId = 1, Credit = "Member One & Member Two", Guests = "Member One & Member Two", Artists = "[]", LastFetched = DateTime.UtcNow });
                c.Insert.IntoTable("AlbumArtistCredits").Row(new { AlbumId = 2, Credit = "Artist Name feat. Guest One", Guests = "Guest One", Artists = "[]", LastFetched = DateTime.UtcNow });
                c.Insert.IntoTable("AlbumArtistCredits").Row(new { AlbumId = 3, Credit = "", Guests = "", Artists = "[]", LastFetched = DateTime.UtcNow });
            });

            var guests = db.Query<AlbumArtistCredit903>("SELECT \"AlbumId\", \"Guests\" FROM \"AlbumArtistCredits\"").ToDictionary(c => c.AlbumId, c => c.Guests);

            guests[1].Should().BeEmpty();
            guests[2].Should().Be("Guest One");
            guests[3].Should().BeEmpty();
        }

        private class AlbumArtistCredit903
        {
            public int AlbumId { get; set; }
            public string Guests { get; set; }
        }
    }
}
