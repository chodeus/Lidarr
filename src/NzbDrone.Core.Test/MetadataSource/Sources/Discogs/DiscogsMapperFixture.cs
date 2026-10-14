using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Sources.Discogs;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Discogs
{
    [TestFixture]
    public class DiscogsMapperFixture : CoreTest
    {
        [TestCase("discogs:artist:3", "artist", 3)]
        [TestCase("Discogs:Master:4", "master", 4)]
        [TestCase("discogs:release:5", "release", 5)]
        public void parses_discogs_ids(string foreignId, string kind, long id)
        {
            DiscogsMapper.TryParseId(foreignId, out var parsedKind, out var parsedId).Should().BeTrue();
            parsedKind.Should().Be(kind);
            parsedId.Should().Be(id);
        }

        [TestCase("discogs:label:3")]
        [TestCase("discogs:artist:-3")]
        [TestCase("discogs:3")]
        [TestCase("deezer:3")]
        [TestCase("11111111-1111-1111-1111-111111111111")]
        public void refuses_anything_else(string foreignId)
        {
            DiscogsMapper.TryParseId(foreignId, out _, out _).Should().BeFalse();
        }

        [Test]
        public void drops_the_number_discogs_adds_to_shared_names()
        {
            DiscogsMapper.WithoutNumber("Placeholder (2)").Should().Be("Placeholder");
            DiscogsMapper.WithoutNumber("Placeholder (Live)").Should().Be("Placeholder (Live)");
        }

        [TestCase("2xCD, EP, Ltd, Promo", "EP", "Promotion", "CD", true)]
        [TestCase("Vinyl, 7\", Single", "Single", "Official", "Vinyl", true)]
        [TestCase("File, MP3, Album", "Album", "Official", "Digital Media", true)]
        [TestCase("CD, Album, Unofficial Release", "Album", "Bootleg", "CD", true)]
        [TestCase("CD, Unofficial", "Album", "Bootleg", "CD", true)]
        [TestCase("CD, EP, Promo, Unofficial Release", "EP", "Bootleg", "CD", true)]
        [TestCase("VHS, Promo", "Album", "Promotion", "Unknown", false)]
        [TestCase("Floppy, 3,5\"", "Album", "Official", "Unknown", false)]
        [TestCase("DVD-V, PAL", "Album", "Official", "Unknown", false)]
        [TestCase("Box + 6xDVD-V, NTSC", "Album", "Official", "Unknown", false)]
        [TestCase("VHS, Promo + CD, Single", "Single", "Promotion", "CD", true)]
        [TestCase("6xCD, Album + Box, Comp", "Album", "Official", "CD", true)]
        [TestCase("CD + DVD-V + Unofficial", "Album", "Bootleg", "CD", true)]
        [TestCase("12\", TP", "Album", "Promotion", "12\"", true)]
        [TestCase("7\", W/Lbl", "Album", "Promotion", "7\"", true)]
        [TestCase("CD, Maxi", "Single", "Official", "CD", true)]
        public void classifies_an_artist_list_format(string format, string primaryType, string status, string mediaFormat, bool isAudio)
        {
            var kind = DiscogsMapper.Classify(format);

            kind.PrimaryType.Should().Be(primaryType);
            kind.ReleaseStatus.Should().Be(status);
            kind.MediaFormat.Should().Be(mediaFormat);
            kind.IsAudio.Should().Be(isAudio);
        }

        [TestCase("CD, Comp")]
        [TestCase("LP, Album, RE + LP, Album, RE + Comp, Ltd")]
        public void the_artist_list_shortens_compilation(string format)
        {
            DiscogsMapper.Classify(format).SecondaryTypes.Should().ContainSingle(t => t.Name == "Compilation");
        }

        [TestCase("DVD", "DVD-Audio", true, "DVD-Audio")]
        [TestCase("Blu-ray", "Blu-ray Audio", true, "Blu-ray Audio")]
        [TestCase("DVD", "DVD-Video", false, "Unknown")]
        public void audio_descriptions_make_video_media_audio(string name, string description, bool isAudio, string mediaFormat)
        {
            var kind = DiscogsMapper.Classify(new List<DiscogsFormatResource> { new() { Name = name, Descriptions = new List<string> { description, "Album" } } });

            kind.IsAudio.Should().Be(isAudio);
            kind.MediaFormat.Should().Be(mediaFormat);
        }

        [TestCase("DVD-Audio, Album", true)]
        [TestCase("DVD-Video, Album", false)]
        public void a_version_is_classified_by_its_descriptions_too(string format, bool isAudio)
        {
            DiscogsMapper.Classify(new DiscogsVersionResource { MajorFormats = new List<string> { "DVD" }, Format = format }).IsAudio.Should().Be(isAudio);
        }

        [Test]
        public void a_box_is_not_the_media_format()
        {
            DiscogsMapper.Classify(new List<DiscogsFormatResource> { new() { Name = "Box Set" }, new() { Name = "CD" } }).MediaFormat.Should().Be("CD");
        }

        [Test]
        public void a_compilation_is_an_album_with_the_compilation_type()
        {
            var kind = DiscogsMapper.Classify(new List<DiscogsFormatResource> { new() { Name = "CD", Descriptions = new List<string> { "Compilation" } } });

            kind.PrimaryType.Should().Be("Album");
            kind.SecondaryTypes.Should().ContainSingle(t => t.Name == "Compilation");
        }

        [Test]
        public void a_set_with_audio_and_video_is_audio()
        {
            DiscogsMapper.Classify(new List<DiscogsFormatResource> { new() { Name = "DVD" }, new() { Name = "CD" } }).IsAudio.Should().BeTrue();
        }

        [TestCase("A1", 1)]
        [TestCase("B4", 1)]
        [TestCase("C1", 2)]
        [TestCase("D", 2)]
        [TestCase("1-01", 1)]
        [TestCase("2-05", 2)]
        [TestCase("2.3", 2)]
        [TestCase("CD2-3", 2)]
        [TestCase("3", 1)]
        [TestCase("", 1)]
        public void reads_the_disc_from_the_position(string position, int medium)
        {
            DiscogsMapper.MediumNumber(position).Should().Be(medium);
        }

        [TestCase("4:44", 284000)]
        [TestCase("1:02:03", 3723000)]
        [TestCase("", 0)]
        [TestCase("4.44", 0)]
        public void reads_durations(string duration, int milliseconds)
        {
            DiscogsMapper.DurationMs(duration).Should().Be(milliseconds);
        }

        [Test]
        public void a_release_skips_headings_and_fills_unknown_date_parts()
        {
            var release = new DiscogsReleaseResource
            {
                Id = 7,
                Title = "Placeholder Album",
                Released = "1997-06-00",
                Country = "UK",
                Artists = new List<DiscogsArtistCreditResource> { new() { Id = 3, Name = "Placeholder Artist (2)" } },
                Labels = new List<DiscogsLabelResource> { new() { Name = "Placeholder Label (3)", Catno = "PL 1" } },
                Formats = new List<DiscogsFormatResource> { new() { Name = "Vinyl", Descriptions = new List<string> { "LP", "Album" } } },
                Tracklist = new List<DiscogsTrackResource>
                {
                    new() { Position = "", Type = "heading", Title = "Side One" },
                    new() { Position = "A1", Type = "track", Title = "First", Duration = "3:00" },
                    new() { Position = "C1", Type = "track", Title = "Second" }
                }
            };
            var artists = new Dictionary<string, ArtistMetadata> { ["discogs:artist:3"] = DiscogsMapper.MapArtist(release.Artists[0]) };

            var mapped = DiscogsMapper.MapRelease(release, artists);

            mapped.ReleaseDate.Should().Be(new DateTime(1997, 6, 1));
            mapped.Label.Should().Equal("Placeholder Label");
            mapped.Disambiguation.Should().Be("PL 1");
            mapped.Media.Select(m => (m.Number, m.Format)).Should().Equal((1, "Vinyl"), (2, "Vinyl"));
            mapped.Tracks.Value.Select(t => (t.ForeignTrackId, t.TrackNumber, t.MediumNumber, t.Duration)).Should().Equal(
                ("discogs:release:7:1", "A1", 1, 180000),
                ("discogs:release:7:2", "C1", 2, 0));
            artists["discogs:artist:3"].Name.Should().Be("Placeholder Artist");
        }

        [Test]
        public void a_works_tracks_are_read_from_its_index()
        {
            var release = new DiscogsReleaseResource
            {
                Id = 8,
                Title = "Placeholder Works",
                Artists = new List<DiscogsArtistCreditResource> { new() { Id = 3, Name = "Placeholder Artist" } },
                Tracklist = new List<DiscogsTrackResource>
                {
                    new()
                    {
                        Position = "", Type = "index", Title = "Placeholder Work", Artists = new List<DiscogsArtistCreditResource> { new() { Id = 5, Name = "Placeholder Ensemble" } },
                        SubTracks = new List<DiscogsTrackResource>
                        {
                            new() { Position = "A", Type = "track", Title = "First", Artists = new List<DiscogsArtistCreditResource> { new() { Id = 4, Name = "Placeholder Soloist" } } },
                            new() { Position = "B", Type = "track", Title = "Second" }
                        }
                    },
                    new() { Position = "C", Type = "track", Title = "Third" }
                }
            };
            var artists = DiscogsMapper.Credits(release).DistinctBy(a => a.Id).Select(DiscogsMapper.MapArtist).ToDictionary(a => a.ForeignArtistId);

            var mapped = DiscogsMapper.MapRelease(release, artists);

            mapped.Tracks.Value.Select(t => (t.Title, t.TrackNumber, t.ArtistMetadata.Value.ForeignArtistId)).Should().Equal(
                ("Placeholder Work: First", "A", "discogs:artist:4"),
                ("Placeholder Work: Second", "B", "discogs:artist:5"),
                ("Third", "C", "discogs:artist:3"));
        }
    }
}
