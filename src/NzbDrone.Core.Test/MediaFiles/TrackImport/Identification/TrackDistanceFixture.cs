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
    public class TrackDistanceFixture : CoreTest
    {
        private Track GivenTrack(string title)
        {
            var artist = Builder<ArtistMetadata>
                .CreateNew()
                .With(x => x.Name = "artist")
                .Build();

            var mbTrack = Builder<Track>
                .CreateNew()
                .With(x => x.Title = title)
                .With(x => x.ArtistMetadata = artist)
                .Build();

            return mbTrack;
        }

        private LocalTrack GivenLocalTrack(Track track)
        {
            var fileInfo = Builder<ParsedTrackInfo>
                .CreateNew()
                .With(x => x.Title = track.Title)
                .With(x => x.CleanTitle = track.Title.CleanTrackTitle())
                .With(x => x.ArtistTitle = track.ArtistMetadata.Value.Name)
                .With(x => x.TrackNumbers = new[] { 1 })
                .With(x => x.RecordingMBId = track.ForeignRecordingId)
                .Build();

            var localTrack = Builder<LocalTrack>
                .CreateNew()
                .With(x => x.FileTrackInfo = fileInfo)
                .Build();

            return localTrack;
        }

        [Test]
        public void test_identical_tracks()
        {
            var track = GivenTrack("one");
            var localTrack = GivenLocalTrack(track);

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().Be(0.0);
        }

        [TestCase("22222222-2222-2222-2222-222222222222", false)]
        [TestCase("other:1", true)]
        public void a_recording_id_from_the_file_counts_only_against_a_musicbrainz_track(string recordingId, bool identical)
        {
            var track = GivenTrack("one");
            track.ForeignRecordingId = recordingId;
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.RecordingMBId = "11111111-1111-1111-1111-111111111111";

            var distance = DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance();

            (distance == 0.0).Should().Be(identical);
        }

        [Test]
        public void another_taggers_id_in_the_file_does_not_count_against_a_musicbrainz_track()
        {
            var track = GivenTrack("one");
            track.ForeignRecordingId = "22222222-2222-2222-2222-222222222222";
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.RecordingMBId = "other:1";

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().Be(0.0);
        }

        [TestCase("22222222-2222-2222-2222-222222222222", false)]
        [TestCase("other:1", true)]
        public void fingerprints_count_only_against_a_musicbrainz_track(string recordingId, bool identical)
        {
            var track = GivenTrack("one");
            track.ForeignRecordingId = recordingId;
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.RecordingMBId = null;
            localTrack.AcoustIdResults = new List<string> { "11111111-1111-1111-1111-111111111111" };

            var distance = DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance();

            (distance == 0.0).Should().Be(identical);
        }

        [Test]
        public void test_feat_removed_from_localtrack()
        {
            var track = GivenTrack("one");
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.Title = "one (feat. two)";

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().Be(0.0);
        }

        [Test]
        public void test_different_title()
        {
            var track = GivenTrack("one");
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.CleanTitle = "foo";

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().NotBe(0.0);
        }

        [Test]
        public void test_different_artist()
        {
            var track = GivenTrack("one");
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.ArtistTitle = "foo";

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().NotBe(0.0);
        }

        [Test]
        public void test_various_artists_tolerated()
        {
            var track = GivenTrack("one");
            var localTrack = GivenLocalTrack(track);
            localTrack.FileTrackInfo.ArtistTitle = "Various Artists";

            DistanceCalculator.TrackDistance(localTrack, track, 1, true).NormalizedDistance().Should().Be(0.0);
        }

        private static double FingerprintPenalty(Track track, List<AcoustIdRecording> recordings)
        {
            var localTrack = new LocalTrack
            {
                FileTrackInfo = new ParsedTrackInfo { CleanTitle = track.Title, TrackNumbers = new[] { 0 } },
                AcoustIdResults = recordings?.Select(x => x.Id).ToList() ?? new List<string> { "sibling" },
                AcoustIdRecordings = recordings
            };

            return DistanceCalculator.TrackDistance(localTrack, track, 1).Penalties["recording_id"].Single();
        }

        private const string ArtistId = "00000000-0000-0000-0000-0000000000aa";

        private Track GivenTimedTrack(string title, int milliseconds)
        {
            var track = GivenTrack(title);
            track.ForeignRecordingId = "11111111-1111-1111-1111-111111111111";
            track.Duration = milliseconds;
            track.ArtistMetadata.Value.ForeignArtistId = ArtistId;

            return track;
        }

        private static List<AcoustIdRecording> Named(string title, double? seconds, params string[] artistIds)
        {
            var artists = (artistIds.Any() ? artistIds : new[] { ArtistId }).Select(x => new AcoustIdArtist { Id = x }).ToList();

            return new List<AcoustIdRecording> { new AcoustIdRecording { Id = "sibling", Title = title, Duration = seconds, Artists = artists } };
        }

        [TestCase("one", 200)]
        [TestCase("One!", 200)]
        [TestCase("Oné", 200)]
        [TestCase("one", 203)]
        [TestCase("one", 197)]
        public void should_give_a_named_recording_with_the_tracks_title_and_length_the_sibling_penalty(string title, double seconds)
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, Named(title, seconds)).Should().Be(0.2);
        }

        [TestCase("two", 200)]
        [TestCase("one (instrumental)", 200)]
        [TestCase("", 200)]
        [TestCase("one", 203.5)]
        [TestCase("one", 196.5)]
        [TestCase("one", 210)]
        [TestCase("one", null)]
        public void should_penalise_a_named_recording_with_another_title_or_length(string title, double? seconds)
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, Named(title, seconds)).Should().Be(1.0);
        }

        [Test]
        public void should_give_a_recording_credited_to_the_track_artist_among_others_the_sibling_penalty()
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, Named("one", 200, "00000000-0000-0000-0000-0000000000bb", ArtistId)).Should().Be(0.2);
        }

        [Test]
        public void should_penalise_another_artists_recording_with_the_tracks_title_and_length()
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, Named("one", 200, "00000000-0000-0000-0000-0000000000bb")).Should().Be(1.0);
        }

        [Test]
        public void should_penalise_a_named_recording_without_artists()
        {
            var track = GivenTimedTrack("one", 200000);
            var recordings = Named("one", 200);
            recordings[0].Artists = null;

            FingerprintPenalty(track, recordings).Should().Be(1.0);
        }

        [TestCase("バラ", "バラ", 0.2)]
        [TestCase("バラ", "ハラ", 1.0)]
        [TestCase("ปี", "ปิ", 1.0)]
        [TestCase("काला", "किला", 1.0)]
        [TestCase("கால்", "கல்", 1.0)]
        public void should_keep_marks_outside_latin_letters_when_comparing_titles(string trackTitle, string recordingTitle, double penalty)
        {
            var track = GivenTimedTrack(trackTitle, 200000);

            FingerprintPenalty(track, Named(recordingTitle, 200)).Should().Be(penalty);
        }

        [Test]
        public void should_penalise_a_named_recording_when_the_track_has_no_length()
        {
            var track = GivenTimedTrack("one", 0);

            FingerprintPenalty(track, Named("one", 0)).Should().Be(1.0);
        }

        [Test]
        public void should_penalise_a_named_recording_without_a_length_against_a_short_track()
        {
            var track = GivenTimedTrack("one", 2000);

            FingerprintPenalty(track, Named("one", null)).Should().Be(1.0);
        }

        [Test]
        public void should_penalise_a_named_recording_when_neither_has_a_title()
        {
            var track = GivenTimedTrack("", 200000);

            FingerprintPenalty(track, Named("", 200)).Should().Be(1.0);
        }

        [Test]
        public void should_penalise_a_named_recording_without_its_title_and_length()
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, null).Should().Be(1.0);
        }

        [Test]
        public void should_not_penalise_the_tracks_own_recording()
        {
            var track = GivenTimedTrack("one", 200000);

            FingerprintPenalty(track, new List<AcoustIdRecording> { new AcoustIdRecording { Id = track.ForeignRecordingId } }).Should().Be(0.0);
        }
    }
}
