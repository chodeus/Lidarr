using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class SiblingRecordingFixture : CoreTest<IdentificationService>
    {
        private static string RecordingId(int n)
        {
            return $"00000000-0000-0000-0000-{n:D12}";
        }

        private static Track GivenTrack(string recordingId, string title, int seconds, int number)
        {
            return new Track
            {
                ForeignRecordingId = recordingId,
                Title = title,
                Duration = seconds * 1000,
                AbsoluteTrackNumber = number,
                MediumNumber = 1,
                OldForeignRecordingIds = new List<string>(),
                ArtistMetadata = new ArtistMetadata { Name = "artist" }
            };
        }

        private static LocalTrack GivenFile(string recordingId, string title, int seconds, int number)
        {
            return new LocalTrack
            {
                Path = $"/music/{recordingId}.flac",
                FileTrackInfo = new ParsedTrackInfo
                {
                    CleanTitle = title,
                    ArtistTitle = "artist",
                    AlbumTitle = "album",
                    TrackNumbers = new[] { number },
                    Duration = TimeSpan.FromSeconds(seconds)
                },
                AcoustIdResults = new List<string> { recordingId },
                AcoustIdRecordings = new List<AcoustIdRecording> { new AcoustIdRecording { Id = recordingId, Title = title, Duration = seconds } }
            };
        }

        private static AlbumRelease GivenRelease(string country, List<Track> tracks)
        {
            return new AlbumRelease
            {
                Title = "album",
                Album = new Album { Title = "album", ArtistMetadata = new ArtistMetadata { Name = "artist", ForeignArtistId = "artist" } },
                Country = new List<string> { country },
                Label = new List<string>(),
                Media = new List<Medium> { new Medium { Format = "Digital Media", Number = 1 } },
                OldForeignReleaseIds = new List<string>(),
                Tracks = tracks
            };
        }

        private double ReleaseDistance(List<LocalTrack> files, AlbumRelease release)
        {
            return DistanceCalculator.AlbumReleaseDistance(files, release, Subject.MapReleaseTracks(files, release.Tracks.Value)).NormalizedDistance();
        }

        [Test]
        public void should_map_same_titled_tracks_to_their_own_recordings()
        {
            var first = GivenTrack(RecordingId(1), "Intro", 60, 1);
            var second = GivenTrack(RecordingId(2), "Intro", 62, 2);
            var firstFile = GivenFile(RecordingId(1), "Intro", 60, 0);
            var secondFile = GivenFile(RecordingId(2), "Intro", 62, 0);

            var mapping = Subject.MapReleaseTracks(new List<LocalTrack> { firstFile, secondFile }, new List<Track> { second, first });

            mapping.Mapping[firstFile].Item1.Should().BeSameAs(first);
            mapping.Mapping[secondFile].Item1.Should().BeSameAs(second);
        }

        [Test]
        public void should_rank_the_release_holding_the_named_recording_above_a_sibling_release()
        {
            var files = new List<LocalTrack> { GivenFile(RecordingId(1), "Song", 200, 1) };
            var named = GivenRelease("United States", new List<Track> { GivenTrack(RecordingId(1), "Song", 200, 1) });
            var sibling = GivenRelease("United States", new List<Track> { GivenTrack(RecordingId(2), "Song", 205, 1) });

            ReleaseDistance(files, named).Should().BeLessThan(ReleaseDistance(files, sibling));
        }

        [Test]
        public void should_rank_an_album_holding_the_named_recordings_above_a_sibling_release_in_a_preferred_country()
        {
            var files = Enumerable.Range(1, 5).Select(n => GivenFile(RecordingId(n), $"Song {n}", 200, n)).ToList();
            var named = GivenRelease("Japan", Enumerable.Range(1, 5).Select(n => GivenTrack(RecordingId(n), $"Song {n}", 200, n)).ToList());
            var sibling = GivenRelease("United States", Enumerable.Range(1, 5).Select(n => GivenTrack(RecordingId(n + 10), $"Song {n}", 203, n)).ToList());

            ReleaseDistance(files, named).Should().BeLessThan(ReleaseDistance(files, sibling));
        }

        [Test]
        public void should_keep_a_file_named_under_a_sibling_within_the_track_threshold()
        {
            var file = GivenFile(RecordingId(2), "Song", 200, 1);
            var track = GivenTrack(RecordingId(1), "Song", 200, 1);

            DistanceCalculator.TrackDistance(file, track, 1).NormalizedDistance().Should().BeLessOrEqualTo(0.4);
        }

        [Test]
        public void should_keep_an_album_named_under_siblings_within_the_album_threshold()
        {
            var files = Enumerable.Range(1, 15).Select(n => GivenFile(RecordingId(n + 100), $"Song {n}", 200, n)).ToList();
            var release = GivenRelease("United States", Enumerable.Range(1, 15).Select(n => GivenTrack(RecordingId(n), $"Song {n}", 200, n)).ToList());

            ReleaseDistance(files, release).Should().BeLessOrEqualTo(0.2);
        }
    }
}
