using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class AlbumDistanceFixture : CoreTest
    {
        private ArtistMetadata _artist;

        [SetUp]
        public void Setup()
        {
            _artist = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "artist")
                .Build();
        }

        private List<Track> GivenTracks(int count)
        {
             return Builder<Track>
                .CreateListOfSize(count)
                .All()
                .With(x => x.ArtistMetadata = _artist)
                .With(x => x.MediumNumber = 1)
                .Build()
                .ToList();
        }

        private LocalTrack GivenLocalTrack(Track track, AlbumRelease release)
        {
            var fileInfo = Builder<ParsedTrackInfo>
                .CreateNew()
                .With(x => x.Title = track.Title)
                .With(x => x.CleanTitle = track.Title.CleanTrackTitle())
                .With(x => x.AlbumTitle = release.Title)
                .With(x => x.Disambiguation = release.Disambiguation)
                .With(x => x.ReleaseMBId = release.ForeignReleaseId)
                .With(x => x.ArtistTitle = track.ArtistMetadata.Value.Name)
                .With(x => x.TrackNumbers = new[] { track.AbsoluteTrackNumber })
                .With(x => x.DiscCount = release.Media.Count)
                .With(x => x.DiscNumber = track.MediumNumber)
                .With(x => x.RecordingMBId = track.ForeignRecordingId)
                .With(x => x.Country = IsoCountries.Find("US"))
                .With(x => x.Label = release.Label.First())
                .With(x => x.Year = (uint)(release.Album.Value.ReleaseDate?.Year ?? 0))
                .Build();

            var localTrack = Builder<LocalTrack>
                .CreateNew()
                .With(x => x.FileTrackInfo = fileInfo)
                .Build();

            return localTrack;
        }

        private List<LocalTrack> GivenLocalTracks(List<Track> tracks, AlbumRelease release)
        {
            var output = new List<LocalTrack>();
            foreach (var track in tracks)
            {
                output.Add(GivenLocalTrack(track, release));
            }

            return output;
        }

        private AlbumRelease GivenAlbumRelease(string title, List<Track> tracks)
        {
            var album = Builder<Album>
                .CreateNew()
                .With(x => x.Title = title)
                .With(x => x.ArtistMetadata = _artist)
                .Build();

            var media = Builder<Medium>
                .CreateListOfSize(tracks.Max(x => x.MediumNumber))
                .Build()
                .ToList();

            return Builder<AlbumRelease>
                .CreateNew()
                .With(x => x.Tracks = tracks)
                .With(x => x.Title = title)
                .With(x => x.Album = album)
                .With(x => x.Media = media)
                .With(x => x.Country = new List<string> { "United States" })
                .With(x => x.Label = new List<string> { "label" })
                .Build();
        }

        private TrackMapping GivenMapping(List<LocalTrack> local, List<Track> remote)
        {
            var mapping = new TrackMapping();
            var distances = local.Zip(remote, (l, r) => Tuple.Create(r, DistanceCalculator.TrackDistance(l, r, DistanceCalculator.GetTotalTrackNumber(r, remote))));
            mapping.Mapping = local.Zip(distances, (l, r) => new { l, r }).ToDictionary(x => x.l, x => x.r);
            mapping.LocalExtra = local.Except(mapping.Mapping.Keys).ToList();
            mapping.MBExtra = remote.Except(mapping.Mapping.Values.Select(x => x.Item1)).ToList();

            return mapping;
        }

        [Test]
        public void test_identical_albums()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().Be(0.0);
        }

        [Test]
        public void test_incomplete_album()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            localTracks.RemoveAt(1);
            var mapping = GivenMapping(localTracks, tracks);

            var dist = DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping);
            dist.NormalizedDistance().Should().NotBe(0.0);
            dist.NormalizedDistance().Should().BeLessThan(0.2);
        }

        [Test]
        public void test_global_artists_differ()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ArtistMetadata = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "different artist")
                .Build();

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().NotBe(0.0);
        }

        [Test]
        public void should_match_files_tagged_with_an_artist_alias()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ArtistMetadata = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "renamed artist")
                .With(x => x.Aliases = new List<string> { "other alias", "artist" })
                .Build();

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().Be(0.0);
        }

        [Test]
        public void should_still_penalise_an_artist_that_matches_no_alias()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ArtistMetadata = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "renamed artist")
                .With(x => x.Aliases = new List<string> { "other alias" })
                .Build();

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().NotBe(0.0);
        }

        private double DistanceWithArtistTag(string artistTag, string releaseArtist = "artist")
        {
            _artist.Name = releaseArtist;

            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            localTracks.ForEach(t => t.FileTrackInfo.ArtistTitle = artistTag);

            return DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance();
        }

        [TestCase("artist;guest")]
        [TestCase("guest; artist")]
        [TestCase("artist / guest")]
        [TestCase("guest, artist")]
        [TestCase("artist & guest")]
        [TestCase("artist feat. guest")]
        [TestCase("artist ft. guest")]
        [TestCase("artist featuring guest")]
        public void should_score_a_tag_crediting_several_artists_closer_than_the_same_text_as_one_name(string artistTag)
        {
            var runTogether = new string(artistTag.Where(char.IsLetterOrDigit).ToArray());

            DistanceWithArtistTag(artistTag).Should().BeLessThan(DistanceWithArtistTag(runTogether));
            DistanceWithArtistTag(artistTag).Should().BeLessThan(DistanceWithArtistTag(artistTag, "other band"));
        }

        [TestCase("Artist & Band", "Artist & Band", "Band")]
        [TestCase("Artist/Band", "Artist/Band", "Band")]
        [TestCase("Artist, Band & Orchestra", "Artist, Band & Orchestra", "Orchestra")]
        [TestCase("Artist & the Band", "Artist and the Band", "the Band")]
        [TestCase("Tia & Lu", "Tia and Lu", "Lu")]
        [TestCase("Artist & Band", "Artist & The Band", "Band")]
        [TestCase("Tia and Lu", "Tia & Lu", "Lu")]
        public void should_rank_the_whole_credit_above_one_of_its_artists(string artistTag, string wholeCredit, string oneArtist)
        {
            DistanceWithArtistTag(artistTag, wholeCredit).Should().BeLessThan(DistanceWithArtistTag(artistTag, oneArtist));
        }

        [TestCase("artist feat. guest", "guest")]
        [TestCase("artist ft guest", "guest")]
        [TestCase("artist featuring guest", "guest")]
        [TestCase("artist (feat. guest & other)", "other")]
        [TestCase("artist feat.guest & other", "other")]
        [TestCase("artist [ft. guest, other]", "other")]
        public void should_rank_the_main_artist_above_a_featured_guest(string artistTag, string guest)
        {
            DistanceWithArtistTag(artistTag, "artist").Should().BeLessThan(DistanceWithArtistTag(artistTag, guest));
        }

        [TestCase("Artist & Band")]
        [TestCase("Artist/Band")]
        [TestCase("Artist, Band & Orchestra")]
        [TestCase("Band Feat")]
        public void should_match_an_artist_whose_name_contains_a_separator(string name)
        {
            DistanceWithArtistTag(name, name).Should().Be(0.0);
        }

        [Test]
        public void should_still_match_a_name_written_without_its_ampersand()
        {
            DistanceWithArtistTag("Tia & Lu", "TiaLu").Should().Be(0.0);
        }

        [TestCase("guest;other guest")]
        [TestCase("guest & other guest")]
        public void should_still_penalise_a_list_that_names_another_artist(string artistTag)
        {
            DistanceWithArtistTag(artistTag).Should().NotBe(0.0);
        }

        [TestCase("https://example.com")]
        [TestCase("www.example.com")]
        [TestCase("example.net")]
        [TestCase("http://example.org/")]
        [TestCase(" example.com ")]
        public void should_not_score_a_web_address_in_the_artist_tag(string artistTag)
        {
            DistanceWithArtistTag(artistTag, "other band").Should().Be(0.0);
        }

        [TestCase("artist - www.example.com")]
        [TestCase("artist @ example.net/")]
        [TestCase("artist | example.ru")]
        public void should_still_score_a_name_followed_by_a_web_address(string artistTag)
        {
            DistanceWithArtistTag(artistTag).Should().BeLessThan(DistanceWithArtistTag(artistTag, "other band"));
        }

        [TestCase("d.j.name")]
        [TestCase("M.C. Other")]
        [TestCase("")]
        public void should_still_penalise_an_artist_tag_that_is_not_a_web_address(string artistTag)
        {
            DistanceWithArtistTag(artistTag).Should().NotBe(0.0);
        }

        [Test]
        public void test_comp_track_artists_match()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ArtistMetadata = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "Various Artists")
                .With(x => x.ForeignArtistId = "89ad4ac3-39f7-470e-963a-56509c546377")
                .Build();

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().Be(0.0);
        }

        // TODO: there are a couple more VA tests in beets but we don't support VA yet anyway
        [Test]
        public void test_tracks_out_of_order()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            localTracks = new[] { 1, 3, 2 }.Select(x => localTracks[x - 1]).ToList();
            var mapping = GivenMapping(localTracks, tracks);

            var dist = DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping);
            dist.NormalizedDistance().Should().NotBe(0.0);
            dist.NormalizedDistance().Should().BeLessThan(0.2);
        }

        [Test]
        public void test_two_medium_release()
        {
            var tracks = GivenTracks(3);
            tracks[2].AbsoluteTrackNumber = 1;
            tracks[2].MediumNumber = 2;
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().Be(0.0);
        }

        [Test]
        public void test_absolute_track_numbering()
        {
            var tracks = GivenTracks(3);
            tracks[2].AbsoluteTrackNumber = 1;
            tracks[2].MediumNumber = 2;
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            localTracks[2].FileTrackInfo.DiscNumber = 2;
            localTracks[2].FileTrackInfo.TrackNumbers = new[] { 3 };

            var mapping = GivenMapping(localTracks, tracks);

            DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance().Should().Be(0.0);
        }

        private static DateTime?[] dates = new DateTime?[] { null, new DateTime(2007, 1, 1), DateTime.Now };

        [TestCaseSource("dates")]
        public void test_null_album_year(DateTime? releaseDate)
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ReleaseDate = null;
            release.ReleaseDate = releaseDate;

            var result = DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance();

            if (!releaseDate.HasValue || (localTracks[0].FileTrackInfo.Year == (releaseDate?.Year ?? 0)))
            {
                result.Should().Be(0.0);
            }
            else
            {
                result.Should().NotBe(0.0);
            }
        }

        [TestCaseSource("dates")]
        public void test_null_release_year(DateTime? albumDate)
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var mapping = GivenMapping(localTracks, tracks);

            release.Album.Value.ReleaseDate = albumDate;
            release.ReleaseDate = null;

            var result = DistanceCalculator.AlbumReleaseDistance(localTracks, release, mapping).NormalizedDistance();

            if (!albumDate.HasValue || (localTracks[0].FileTrackInfo.Year == (albumDate?.Year ?? 0)))
            {
                result.Should().Be(0.0);
            }
            else
            {
                result.Should().NotBe(0.0);
            }
        }
    }
}
