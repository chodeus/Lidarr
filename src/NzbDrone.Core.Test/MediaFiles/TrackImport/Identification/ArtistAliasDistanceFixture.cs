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
    // The real DistanceCalculator and Distance over built releases, with no mocks
    [TestFixture]
    public class ArtistAliasDistanceFixture : CoreTest
    {
        private static ArtistMetadata Artist(string name, params string[] aliases)
        {
            return Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = name)
                .With(x => x.ForeignArtistId = Guid.NewGuid().ToString())
                .With(x => x.Aliases = aliases.ToList())
                .Build();
        }

        private static AlbumRelease Release(ArtistMetadata artist, string title)
        {
            var tracks = Builder<Track>
                .CreateListOfSize(3)
                .All()
                .With(x => x.ArtistMetadata = artist)
                .With(x => x.MediumNumber = 1)
                .Build()
                .ToList();

            var album = Builder<Album>
                .CreateNew()
                .With(x => x.Title = title)
                .With(x => x.ArtistMetadata = artist)
                .Build();

            return Builder<AlbumRelease>
                .CreateNew()
                .With(x => x.Tracks = tracks)
                .With(x => x.Title = title)
                .With(x => x.Album = album)
                .With(x => x.Media = Builder<Medium>.CreateListOfSize(1).Build().ToList())
                .With(x => x.Country = new List<string> { "United States" })
                .With(x => x.Label = new List<string> { "label" })
                .Build();
        }

        private static List<LocalTrack> LocalTracks(AlbumRelease release, string artistTag)
        {
            return release.Tracks.Value.Select(track => Builder<LocalTrack>
                .CreateNew()
                .With(x => x.FileTrackInfo = Builder<ParsedTrackInfo>
                    .CreateNew()
                    .With(f => f.Title = track.Title)
                    .With(f => f.CleanTitle = track.Title.CleanTrackTitle())
                    .With(f => f.AlbumTitle = release.Title)
                    .With(f => f.Disambiguation = release.Disambiguation)
                    .With(f => f.ArtistTitle = artistTag)
                    .With(f => f.TrackNumbers = new[] { track.AbsoluteTrackNumber })
                    .With(f => f.DiscCount = 1)
                    .With(f => f.DiscNumber = 1)
                    .With(f => f.RecordingMBId = null)
                    .With(f => f.Country = IsoCountries.Find("US"))
                    .With(f => f.Label = "label")
                    .With(f => f.Year = (uint)(release.Album.Value.ReleaseDate?.Year ?? 0))
                    .Build())
                .Build()).ToList();
        }

        private static Distance Score(List<LocalTrack> local, AlbumRelease release)
        {
            var remote = release.Tracks.Value;
            var mapping = new TrackMapping();
            mapping.Mapping = local.Zip(remote, (l, r) => new { l, r })
                .ToDictionary(x => x.l, x => Tuple.Create(x.r, DistanceCalculator.TrackDistance(x.l, x.r, DistanceCalculator.GetTotalTrackNumber(x.r, remote))));
            mapping.LocalExtra = new List<LocalTrack>();
            mapping.MBExtra = new List<Track>();
            return DistanceCalculator.AlbumReleaseDistance(local, release, mapping);
        }

        [TestCase("")]
        [TestCase("†††")]
        [TestCase("!!!")]
        public void should_keep_the_full_artist_penalty_for_untagged_files_when_an_alias_cleans_to_nothing(string alias)
        {
            var release = Release(Artist("Crosses", alias), "album");
            var local = LocalTracks(release, "");

            Score(local, release).Penalties["artist"].Single().Should().Be(1.0);
        }

        // Files tagged "Artist Name", without the "The"; a tribute act has the bare name as an alias
        [Test]
        public void should_not_let_an_alias_another_artist_shares_beat_the_real_artist()
        {
            var real = Release(Artist("The Artist Name"), "Album Title");
            var tribute = Release(Artist("The Tribute Band", "Artist Name"), "Album Title");

            var realScore = Score(LocalTracks(real, "Artist Name"), real).NormalizedDistance();
            var tributeScore = Score(LocalTracks(real, "Artist Name"), tribute).NormalizedDistance();

            tributeScore.Should().BeGreaterThan(realScore);
        }

        [Test]
        public void should_score_no_names_or_aliases_like_a_missing_name()
        {
            var a = new Distance();
            a.AddStringToClosest("artist", "value", new List<string>(), new List<string>());
            var b = new Distance();
            b.AddString("artist", "value", null);
            a.Penalties["artist"].Should().Equal(b.Penalties["artist"]);

            var c = new Distance();
            c.AddStringToClosest("artist", "", new List<string>(), new List<string>());
            c.Penalties["artist"].Single().Should().Be(0.0);
        }

        [Test]
        public void should_handle_a_missing_alias_list_and_missing_aliases()
        {
            var artist = Artist("artist");
            artist.Aliases = null;
            var release = Release(artist, "album");
            Score(LocalTracks(release, "artist"), release).Penalties["artist"].Single().Should().Be(0.0);

            artist.Aliases = new List<string> { null, "x" };
            Score(LocalTracks(release, "artist"), release).Penalties["artist"].Single().Should().Be(0.0);
            Score(LocalTracks(release, "zzzzzz"), release).Penalties["artist"].Single().Should().Be(1.0);
        }

        [Test]
        public void should_match_case_and_accent_variants()
        {
            var release = Release(Artist("ART-IST", "Art Ist"), "album");
            Score(LocalTracks(release, "ÀRT-IST"), release).Penalties["artist"].Single().Should().Be(0.0);
            Score(LocalTracks(release, "art ist"), release).Penalties["artist"].Single().Should().Be(0.0);
        }

        [Test]
        public void should_match_files_tagged_without_the_artists_leading_the()
        {
            var release = Release(Artist("The Artist Name"), "Album Title");

            Score(LocalTracks(release, "Artist Name"), release).Penalties["artist"].Single().Should().Be(0.0);
        }

        [Test]
        public void should_not_match_a_the_tag_to_a_release_artist_without_a_name()
        {
            var release = Release(Artist(""), "album");

            Score(LocalTracks(release, "The"), release).Penalties["artist"].Single().Should().BeGreaterThan(0.0);
        }

        [Test]
        public void should_score_an_alias_match_a_little_worse_than_the_name()
        {
            var release = Release(Artist("Artist Name", "Former Name"), "album");

            Score(LocalTracks(release, "Former Name"), release).Penalties["artist"].Single().Should().BeGreaterThan(0.0).And.BeLessThan(0.1);
        }
    }
}
