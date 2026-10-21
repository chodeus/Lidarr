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
    public class CreditedArtistDistanceFixture : CoreTest
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

        // The same files scored against the right release and a wrong one with the same title and tracks
        private static void RightShouldBeatWrong(string tag, ArtistMetadata right, ArtistMetadata wrong)
        {
            var rightRelease = Release(right, "Greatest Hits");
            var wrongRelease = Release(wrong, "Greatest Hits");
            var local = LocalTracks(rightRelease, tag);

            var rightScore = Score(local, rightRelease);
            var wrongScore = Score(local, wrongRelease);

            rightScore.NormalizedDistance().Should().BeLessThan(wrongScore.NormalizedDistance(),
                "right artist {0} vs wrong {1}",
                string.Join(",", rightScore.Penalties.GetValueOrDefault("artist") ?? new List<double>()),
                string.Join(",", wrongScore.Penalties.GetValueOrDefault("artist") ?? new List<double>()));
        }

        // A featured guest on an artist whose own name contains a separator
        [TestCase("Earth, Wind & Fire feat. The Emotions", "Earth, Wind & Fire", "Earth")]
        [TestCase("Simon & Garfunkel (feat. Paul Desmond)", "Simon & Garfunkel", "Simon")]
        [TestCase("Mumford & Sons feat. Baaba Maal", "Mumford & Sons", "Sons")]
        [TestCase("AC/DC ft. Guest", "AC/DC", "AC")]
        public void should_score_a_name_with_a_separator_whole_when_a_guest_is_featured(string tag, string right, string wrong)
        {
            RightShouldBeatWrong(tag, Artist(right), Artist(wrong));
        }

        [TestCase("Earth, Wind & Fire", "Earth, Wind & Fire", "Earth")]
        [TestCase("Simon & Garfunkel", "Simon & Garfunkel", "Simon")]
        [TestCase("Crosby, Stills, Nash & Young", "Crosby, Stills, Nash & Young", "Young")]
        [TestCase("Earth Wind and Fire", "Earth, Wind & Fire", "Earth")]
        [TestCase("Beyoncé & JAY-Z", "The Carters", "Beyoncé", "Beyoncé & JAY-Z")]
        public void should_rank_the_whole_credit_ahead_of_one_of_its_pieces(string tag, string right, string wrong, string rightAlias = null)
        {
            RightShouldBeatWrong(tag, rightAlias == null ? Artist(right) : Artist(right, rightAlias), Artist(wrong));
        }

        // The tag tells nothing about either artist, so neither release may score worse for it
        [Test]
        public void should_not_score_a_watermark_against_an_artist_with_a_web_like_alias()
        {
            var right = Release(Artist("Right Artist", "rightartist.com"), "Greatest Hits");
            var wrong = Release(Artist("Wrong Artist"), "Greatest Hits");
            var local = LocalTracks(right, "www.music-downloads.ru");

            Score(local, right).Penalties.Should().NotContainKey("artist");
            Score(local, right).NormalizedDistance().Should().Be(Score(local, wrong).NormalizedDistance());
        }

        [Test]
        public void should_not_match_a_piece_that_cleans_to_nothing()
        {
            var release = Release(Artist("!!!"), "Greatest Hits");

            Score(LocalTracks(release, "Somebody Else & ..."), release).Penalties["artist"].Single().Should().BeGreaterThan(0.5);
        }
    }
}
