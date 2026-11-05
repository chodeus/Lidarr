using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class GetCandidatesFixture : CoreTest<CandidateService>
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
                .Build()
                .ToList();
        }

        private ParsedTrackInfo GivenParsedTrackInfo(Track track, AlbumRelease release)
        {
            return Builder<ParsedTrackInfo>
                .CreateNew()
                .With(x => x.Title = track.Title)
                .With(x => x.AlbumTitle = release.Title)
                .With(x => x.Disambiguation = release.Disambiguation)
                .With(x => x.ReleaseMBId = release.ForeignReleaseId)
                .With(x => x.ArtistTitle = track.ArtistMetadata.Value.Name)
                .With(x => x.TrackNumbers = new[] { track.AbsoluteTrackNumber })
                .With(x => x.RecordingMBId = track.ForeignRecordingId)
                .With(x => x.Country = IsoCountries.Find("US"))
                .With(x => x.Label = release.Label.First())
                .With(x => x.Year = (uint)release.Album.Value.ReleaseDate.Value.Year)
                .Build();
        }

        private List<LocalTrack> GivenLocalTracks(List<Track> tracks, AlbumRelease release)
        {
            var output = Builder<LocalTrack>
                .CreateListOfSize(tracks.Count)
                .Build()
                .ToList();

            for (var i = 0; i < tracks.Count; i++)
            {
                output[i].FileTrackInfo = GivenParsedTrackInfo(tracks[i], release);
            }

            return output;
        }

        private AlbumRelease GivenAlbumRelease(string title, List<Track> tracks)
        {
            var album = Builder<Album>
                .CreateNew()
                .With(x => x.Title = title)
                .With(x => x.ArtistMetadata = _artist)
                .With(x => x.AnyReleaseOk = true)
                .Build();

            var media = Builder<Medium>
                .CreateListOfSize(1)
                .Build()
                .ToList();

            return Builder<AlbumRelease>
                .CreateNew()
                .With(x => x.Tracks = tracks)
                .With(x => x.Title = title)
                .With(x => x.Album = album)
                .With(x => x.Media = media)
                .With(x => x.Country = new List<string>())
                .With(x => x.Label = new List<string> { "label" })
                .With(x => x.ForeignReleaseId = null)
                .Build();
        }

        private LocalAlbumRelease GivenLocalAlbumRelease()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);

            return new LocalAlbumRelease(localTracks);
        }

        [Test]
        public void get_candidates_by_fingerprint_should_not_fail_if_fingerprint_lookup_returned_null()
        {
            Mocker.GetMock<IFingerprintingService>()
                .Setup(x => x.Lookup(It.IsAny<List<LocalTrack>>(), It.IsAny<double>()))
                .Callback((List<LocalTrack> x, double thres) =>
                    {
                        foreach (var track in x)
                        {
                            track.AcoustIdResults = null;
                        }
                    });

            Mocker.GetMock<IReleaseService>()
                .Setup(x => x.GetReleasesByRecordingIds(It.IsAny<List<string>>()))
                .Returns(new List<AlbumRelease>());

            var local = GivenLocalAlbumRelease();

            Subject.GetDbCandidatesFromFingerprint(local, null, false).Should().BeEquivalentTo(new List<CandidateAlbumRelease>());
        }

        [Test]
        public void get_candidates_should_only_return_specified_release_if_set()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            var localTracks = GivenLocalTracks(tracks, release);
            var localAlbumRelease = new LocalAlbumRelease(localTracks);
            var idOverrides = new IdentificationOverrides
            {
                AlbumRelease = release
            };

            Subject.GetDbCandidatesFromTags(localAlbumRelease, idOverrides, false).Should().BeEquivalentTo(
                new List<CandidateAlbumRelease> { new CandidateAlbumRelease(release) });
        }

        [Test]
        public void get_candidates_should_use_consensus_release_id()
        {
            var tracks = GivenTracks(3);
            var release = GivenAlbumRelease("album", tracks);
            release.ForeignReleaseId = "xxx";
            var localTracks = GivenLocalTracks(tracks, release);
            var localAlbumRelease = new LocalAlbumRelease(localTracks);

            Mocker.GetMock<IReleaseService>()
                  .Setup(x => x.GetReleaseByForeignReleaseId("xxx", true))
                  .Returns(release);

            Subject.GetDbCandidatesFromTags(localAlbumRelease, null, false).Should().BeEquivalentTo(
                new List<CandidateAlbumRelease> { new CandidateAlbumRelease(release) });
        }

        private (AlbumRelease Monitored, AlbumRelease Other) GivenTwoReleases(bool anyReleaseOk)
        {
            var monitored = GivenAlbumRelease("album", GivenTracks(3));
            var other = GivenAlbumRelease("album", GivenTracks(3));
            other.Album = monitored.Album;
            monitored.Album.Value.AnyReleaseOk = anyReleaseOk;
            monitored.Monitored = true;
            other.Monitored = false;
            other.ForeignReleaseId = "other";

            Mocker.GetMock<IReleaseService>()
                  .Setup(x => x.GetReleasesByAlbum(monitored.Album.Value.Id))
                  .Returns(new List<AlbumRelease> { other, monitored });

            return (monitored, other);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void get_candidates_for_an_album_should_offer_only_its_monitored_release_unless_any_release_ok(bool anyReleaseOk)
        {
            var (monitored, other) = GivenTwoReleases(anyReleaseOk);
            var localAlbumRelease = new LocalAlbumRelease(GivenLocalTracks(other.Tracks.Value, other));
            localAlbumRelease.LocalTracks.ForEach(x => x.FileTrackInfo.ReleaseMBId = null);

            var candidates = Subject.GetDbCandidatesFromTags(localAlbumRelease, new IdentificationOverrides { Album = monitored.Album.Value }, false);

            candidates.Select(x => x.AlbumRelease).Should().BeEquivalentTo(anyReleaseOk ? new[] { other, monitored } : new[] { monitored });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void get_candidates_should_skip_a_tagged_release_its_album_does_not_want(bool anyReleaseOk)
        {
            var (monitored, other) = GivenTwoReleases(anyReleaseOk);
            var localAlbumRelease = new LocalAlbumRelease(GivenLocalTracks(other.Tracks.Value, other));

            Mocker.GetMock<IReleaseService>()
                  .Setup(x => x.GetReleaseByForeignReleaseId("other", true))
                  .Returns(other);

            var candidates = Subject.GetDbCandidatesFromTags(localAlbumRelease, new IdentificationOverrides { Album = monitored.Album.Value }, false);

            candidates.Select(x => x.AlbumRelease).Should().BeEquivalentTo(anyReleaseOk ? new[] { other } : new[] { monitored });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void get_candidates_by_fingerprint_should_skip_a_release_its_album_does_not_want(bool anyReleaseOk)
        {
            var (monitored, other) = GivenTwoReleases(anyReleaseOk);
            var localAlbumRelease = new LocalAlbumRelease(GivenLocalTracks(other.Tracks.Value, other));
            var recordings = other.Tracks.Value.Select(x => x.ForeignRecordingId).ToList();
            monitored.Tracks.Value.ForEach(x => x.ForeignRecordingId = recordings[monitored.Tracks.Value.IndexOf(x)]);
            localAlbumRelease.LocalTracks.ForEach(x => x.AcoustIdResults = recordings);

            Mocker.GetMock<IReleaseService>()
                  .Setup(x => x.GetReleasesByRecordingIds(It.IsAny<List<string>>()))
                  .Returns(new List<AlbumRelease> { other, monitored });

            var candidates = Subject.GetDbCandidatesFromFingerprint(localAlbumRelease, null, false);

            candidates.Select(x => x.AlbumRelease).Should().BeEquivalentTo(anyReleaseOk ? new[] { other, monitored } : new[] { monitored });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void get_remote_candidates_should_skip_a_release_an_existing_album_does_not_want(bool anyReleaseOk)
        {
            var (monitored, other) = GivenTwoReleases(anyReleaseOk);
            var album = monitored.Album.Value;
            album.AlbumReleases = new List<AlbumRelease> { other, monitored };
            var localAlbumRelease = new LocalAlbumRelease(GivenLocalTracks(other.Tracks.Value, other));
            localAlbumRelease.LocalTracks.ForEach(x => x.FileTrackInfo.AlbumMBId = "album-mbid");

            Mocker.GetMock<ISearchForNewAlbum>()
                  .Setup(x => x.SearchForNewAlbum("mbid:album-mbid", null))
                  .Returns(new List<Album> { album });

            var candidates = Subject.GetRemoteCandidates(localAlbumRelease);

            candidates.Select(x => x.AlbumRelease).Should().BeEquivalentTo(anyReleaseOk ? new[] { other, monitored } : new[] { monitored });
        }
    }
}
