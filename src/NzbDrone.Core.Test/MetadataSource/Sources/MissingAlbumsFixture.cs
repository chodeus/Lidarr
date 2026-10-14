using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    [TestFixture]
    public class MissingAlbumsFixture : CoreTest
    {
        private static Album Album(string title, int year)
        {
            return new Album { ForeignAlbumId = title + year, Title = title, ReleaseDate = new DateTime(year, 1, 1) };
        }

        private static IEnumerable<string> Missing(List<Album> known, params Album[] candidates)
        {
            return MissingAlbums.ByIdOrTitle(known, candidates).Select(a => a.Title + " " + a.ReleaseDate?.Year);
        }

        [Test]
        public void adds_an_album_the_artist_lacks()
        {
            Missing(new List<Album> { Album("First Album", 2001) }, Album("Second Album", 2003)).Should().Equal("Second Album 2003");
        }

        [TestCase("First Album", 2001)]
        [TestCase("FIRST ALBUM!", 2002)]
        [TestCase("First Album", 2000)]
        public void skips_the_same_title_within_a_year(string title, int year)
        {
            Missing(new List<Album> { Album("First Album", 2001) }, Album(title, year)).Should().BeEmpty();
        }

        [Test]
        public void adds_the_same_title_from_a_different_time()
        {
            Missing(new List<Album> { Album("Greatest Hits", 1990) }, Album("Greatest Hits", 2005)).Should().Equal("Greatest Hits 2005");
        }

        [TestCase("First Album (Deluxe Edition)", 2011)]
        [TestCase("First Album [Remastered]", 2016)]
        [TestCase("First Album (Collector's Edition)", 2009)]
        public void skips_an_edition_of_an_album_the_artist_has(string title, int year)
        {
            Missing(new List<Album> { Album("First Album", 2001) }, Album(title, year)).Should().BeEmpty();
        }

        [Test]
        public void keeps_only_the_first_of_two_editions_it_adds()
        {
            Missing(new List<Album>(), Album("Lost Album (Deluxe Edition)", 2011), Album("Lost Album (Remastered)", 2016)).Should().Equal("Lost Album (Deluxe Edition) 2011");
        }

        private static Album WithIds(Album album, string id, string oldId)
        {
            album.ForeignAlbumId = id;
            album.OldForeignAlbumIds = oldId == null ? new List<string>() : new List<string> { oldId };

            return album;
        }

        [TestCase("album-1", null, "album-1", null)]
        [TestCase("album-1", "album-0", "album-0", null)]
        [TestCase("album-1", null, "album-2", "album-1")]
        public void skips_an_album_the_artist_has_by_id_under_another_title(string knownId, string knownOldId, string candidateId, string candidateOldId)
        {
            var known = new List<Album> { WithIds(Album("First Album", 2001), knownId, knownOldId) };

            Missing(known, WithIds(Album("Renamed Album", 2005), candidateId, candidateOldId)).Should().BeEmpty();
        }

        [Test]
        public void by_id_adds_an_album_with_a_known_title_and_a_new_id()
        {
            var known = new List<Album> { WithIds(Album("First Album", 2001), "album-1", null) };

            MissingAlbums.ById(known, new[] { WithIds(Album("First Album", 2001), "album-2", null) }).Select(a => a.ForeignAlbumId).Should().Equal("album-2");
        }

        [Test]
        public void keeps_only_the_first_of_two_albums_with_the_same_id()
        {
            Missing(new List<Album>(), WithIds(Album("Lost Album", 2001), "album-5", null), WithIds(Album("Renamed Album", 2005), "album-5", null)).Should().Equal("Lost Album 2001");
        }
    }
}
