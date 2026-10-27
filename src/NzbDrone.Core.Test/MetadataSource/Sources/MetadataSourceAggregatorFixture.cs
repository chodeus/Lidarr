using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.Sources;
using NzbDrone.Core.MetadataSource.Sources.Lidarr;
using NzbDrone.Core.Music;
using NzbDrone.Core.Profiles.MetadataSources;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Sources
{
    [TestFixture]
    public class MetadataSourceAggregatorFixture : CoreTest<MetadataSourceAggregator>
    {
        private const string MusicBrainzId = "11111111-1111-1111-1111-111111111111";
        private const string OtherFamily = "Other";
        private const string OtherId = "other:1";

        private MetadataSourceProfile _profile;
        private List<IMetadataSource> _sources;
        private Mock<LidarrMetadataSource> _lidarr;

        [SetUp]
        public void Setup()
        {
            _profile = new MetadataSourceProfile { Id = 1, Name = "Default" };
            _sources = new List<IMetadataSource>();

            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            _lidarr = new Mock<LidarrMetadataSource>(MockBehavior.Default, new object[] { null }) { CallBase = true };
            Mocker.SetConstant(_lidarr.Object);

            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.GetDefaultProfile()).Returns(() => _profile);
            Mocker.GetMock<IMetadataSourceFactory>().Setup(s => s.GetAvailableProviders()).Returns(() => _sources.ToList());
        }

        private Mock<IMetadataSource> GivenSource(int id, MetadataSourceRole role, string family = MetadataIdFamilies.MusicBrainz, MetadataSourceProfile profile = null)
        {
            var source = new Mock<IMetadataSource>();
            source.SetupGet(s => s.Definition).Returns(new MetadataSourceDefinition { Id = id, Name = "Source " + id });
            source.SetupGet(s => s.IdFamily).Returns(family);
            source.Setup(s => s.CanHandleId(It.IsAny<string>()))
                  .Returns<string>(foreignId => family == MetadataIdFamilies.MusicBrainz ? MusicBrainzIds.IsMusicBrainzId(foreignId) : foreignId.StartsWith("other:"));

            _sources.Add(source.Object);
            (profile ?? _profile).Items.Add(new MetadataSourceProfileItem { MetadataSourceId = id, Role = role });

            return source;
        }

        private static Artist ArtistWith(params string[] albumIds)
        {
            return new Artist
            {
                Metadata = new ArtistMetadata { ForeignArtistId = MusicBrainzId, Name = "Artist Name" },
                Albums = albumIds.Select(id => new Album { ForeignAlbumId = id, Title = "Album Title", OldForeignAlbumIds = new List<string>() }).ToList()
            };
        }

        [Test]
        public void or_returns_the_first_source_that_has_the_artist()
        {
            var first = GivenSource(1, MetadataSourceRole.Primary);
            var second = GivenSource(2, MetadataSourceRole.Primary);
            var artist = ArtistWith("album-1");
            first.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(artist);

            Subject.GetArtistInfo(MusicBrainzId, 1).Should().BeSameAs(artist);

            second.Verify(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void or_asks_the_next_source_when_the_first_does_not_have_the_artist()
        {
            var first = GivenSource(1, MetadataSourceRole.Primary);
            var second = GivenSource(2, MetadataSourceRole.Primary);
            var artist = ArtistWith("album-1");
            first.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));
            second.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(artist);

            Subject.GetArtistInfo(MusicBrainzId, 1).Should().BeSameAs(artist);
        }

        [Test]
        public void reports_the_outage_instead_of_not_found_when_any_source_was_down()
        {
            var first = GivenSource(1, MetadataSourceRole.Primary);
            var second = GivenSource(2, MetadataSourceRole.Primary);
            first.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new TimeoutException("Source 1 is down"));
            second.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));

            Assert.Throws<TimeoutException>(() => Subject.GetArtistInfo(MusicBrainzId, 1));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void reports_not_found_only_when_every_source_says_so()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));
            GivenSource(2, MetadataSourceRole.Fallback).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));

            Assert.Throws<ArtistNotFoundException>(() => Subject.GetArtistInfo(MusicBrainzId, 1));
        }

        [Test]
        public void and_adds_albums_only_the_other_primary_source_has()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            var firstArtist = ArtistWith("album-1", "album-2");
            var secondArtist = ArtistWith("album-2", "album-3");
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(firstArtist);
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(secondArtist);

            var result = Subject.GetArtistInfo(MusicBrainzId, 1);

            result.Should().BeSameAs(firstArtist);
            result.Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("album-1", "album-2", "album-3");
            result.Albums.Value[1].Should().BeSameAs(firstArtist.Albums.Value[1]);
        }

        [Test]
        public void and_reports_the_outage_instead_of_merging_without_a_down_source()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith("album-1"));
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new TimeoutException("Source 2 is down"));

            Assert.Throws<TimeoutException>(() => Subject.GetArtistInfo(MusicBrainzId, 1));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void uses_a_fallback_source_after_the_primary_sources()
        {
            var artist = ArtistWith("album-1");
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));
            GivenSource(2, MetadataSourceRole.Fallback).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(artist);

            Subject.GetArtistInfo(MusicBrainzId, 1).Should().BeSameAs(artist);
        }

        [Test]
        public void routes_an_id_to_the_sources_of_its_family()
        {
            var musicBrainz = GivenSource(1, MetadataSourceRole.Primary);
            var other = GivenSource(2, MetadataSourceRole.Fallback, OtherFamily);
            var artist = ArtistWith("other:album-1");
            other.Setup(s => s.GetArtistInfo(OtherId, 1)).Returns(artist);

            Subject.GetArtistInfo(OtherId, 1).Should().BeSameAs(artist);

            musicBrainz.Verify(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void uses_lidarrs_own_server_when_no_profile_source_can_look_up_a_musicbrainz_id()
        {
            var artist = ArtistWith("album-1");
            _lidarr.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(artist);

            Subject.GetArtistInfo(MusicBrainzId, 1).Should().BeSameAs(artist);
        }

        [Test]
        public void refuses_an_id_no_source_can_look_up()
        {
            GivenSource(1, MetadataSourceRole.Primary);

            Assert.Throws<MetadataSourceUnavailableException>(() => Subject.GetArtistInfo(OtherId, 1));
        }

        private static Tuple<string, Album, List<ArtistMetadata>> AlbumInfo(string artistId, int releases)
        {
            var album = new Album { ForeignAlbumId = MusicBrainzId, AlbumReleases = Enumerable.Range(1, releases).Select(i => new AlbumRelease { ForeignReleaseId = "release-" + i }).ToList() };

            return Tuple.Create(artistId, album, new List<ArtistMetadata>());
        }

        [Test]
        public void album_lookup_returns_one_source_even_when_primaries_combine()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            var album = AlbumInfo("artist-1", 1);
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(album);
            var second = GivenSource(2, MetadataSourceRole.Primary);

            Subject.GetAlbumInfo(MusicBrainzId).Should().BeSameAs(album);

            second.Verify(s => s.GetAlbumInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void album_lookup_asks_the_next_primary_when_the_first_has_no_releases()
        {
            var album = AlbumInfo("artist-2", 1);
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(AlbumInfo("artist-1", 0));
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(album);

            Subject.GetAlbumInfo(MusicBrainzId).Should().BeSameAs(album);
        }

        [Test]
        public void album_lookup_asks_the_fallback_when_the_primaries_have_no_releases()
        {
            var album = AlbumInfo("artist-2", 2);
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(AlbumInfo("artist-1", 0));
            GivenSource(2, MetadataSourceRole.Fallback).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(album);

            Subject.GetAlbumInfo(MusicBrainzId).Should().BeSameAs(album);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void album_lookup_returns_the_first_answer_when_no_source_has_releases(bool fallbackHasIt)
        {
            var first = AlbumInfo("artist-1", 0);
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(first);
            var fallback = GivenSource(2, MetadataSourceRole.Fallback);

            if (fallbackHasIt)
            {
                fallback.Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(AlbumInfo("artist-2", 0));
            }
            else
            {
                fallback.Setup(s => s.GetAlbumInfo(MusicBrainzId)).Throws(new AlbumNotFoundException(MusicBrainzId));
            }

            Subject.GetAlbumInfo(MusicBrainzId).Should().BeSameAs(first);
        }

        [Test]
        public void album_lookup_reports_the_outage_when_the_only_answer_has_no_releases()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Returns(AlbumInfo("artist-1", 0));
            GivenSource(2, MetadataSourceRole.Fallback).Setup(s => s.GetAlbumInfo(MusicBrainzId)).Throws(new TimeoutException("Source 2 is down"));

            Assert.Throws<TimeoutException>(() => Subject.GetAlbumInfo(MusicBrainzId));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void search_returns_the_first_source_with_results_and_labels_them()
        {
            var empty = GivenSource(1, MetadataSourceRole.Primary);
            var found = GivenSource(2, MetadataSourceRole.Primary);
            var artist = ArtistWith();
            empty.Setup(s => s.SearchForNewArtist("Artist Name")).Returns(new List<Artist>());
            found.Setup(s => s.SearchForNewArtist("Artist Name")).Returns(new List<Artist> { artist });

            var results = Subject.SearchForNewArtist("Artist Name");

            results.Should().Equal(artist);
            Subject.LabelFor(artist).Should().Be("Source 2");
        }

        [Test]
        public void search_falls_back_to_other_families_when_the_primary_sources_find_nothing()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.SearchForNewArtist("Artist Name")).Returns(new List<Artist>());
            var artist = ArtistWith();
            GivenSource(2, MetadataSourceRole.Fallback, OtherFamily).Setup(s => s.SearchForNewArtist("Artist Name")).Returns(new List<Artist> { artist });

            Subject.SearchForNewArtist("Artist Name").Should().Equal(artist);
        }

        [Test]
        public void search_reports_the_outage_when_nothing_was_found()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.SearchForNewArtist("Artist Name")).Throws(new TimeoutException("Source 1 is down"));
            GivenSource(2, MetadataSourceRole.Fallback, OtherFamily).Setup(s => s.SearchForNewArtist("Artist Name")).Returns(new List<Artist>());

            Assert.Throws<TimeoutException>(() => Subject.SearchForNewArtist("Artist Name"));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void changed_artists_combine_every_source()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetChangedArtists(It.IsAny<DateTime>())).Returns(new HashSet<string> { "artist-1" });
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetChangedArtists(It.IsAny<DateTime>())).Returns(new HashSet<string> { "artist-2" });

            // Neither is Lidarr's own source, so MusicBrainz lookups can still fall back to Lidarr's server
            _lidarr.Setup(s => s.GetChangedArtists(It.IsAny<DateTime>())).Returns(new HashSet<string> { "artist-3" });

            Subject.GetChangedArtists(DateTime.UtcNow).Should().BeEquivalentTo(new[] { "artist-1", "artist-2", "artist-3" });
        }

        [Test]
        public void changed_artists_are_unknown_when_any_source_cannot_list_them()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetChangedArtists(It.IsAny<DateTime>())).Returns(new HashSet<string> { "artist-1" });
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetChangedArtists(It.IsAny<DateTime>())).Returns((HashSet<string>)null);

            Subject.GetChangedArtists(DateTime.UtcNow).Should().BeNull();
        }

        [Test]
        public void uses_the_profile_matching_the_artists_tags()
        {
            var tagged = new MetadataSourceProfile { Id = 2, Name = "Tagged", Tags = new HashSet<int> { 5 } };
            GivenSource(1, MetadataSourceRole.Primary);
            var taggedSource = GivenSource(2, MetadataSourceRole.Primary, profile: tagged);
            var artist = ArtistWith("album-1");
            taggedSource.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(artist);

            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.HasTaggedProfiles()).Returns(true);
            Mocker.GetMock<IArtistService>().Setup(s => s.FindById(MusicBrainzId)).Returns(new Artist { Tags = new HashSet<int> { 5 } });
            Mocker.GetMock<IMetadataSourceProfileService>().Setup(s => s.BestForTags(It.Is<HashSet<int>>(t => t.Contains(5)))).Returns(tagged);

            Subject.GetArtistInfo(MusicBrainzId, 1).Should().BeSameAs(artist);
        }

        [Test]
        public void skips_the_artist_lookup_when_no_profile_has_tags()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith());

            Subject.GetArtistInfo(MusicBrainzId, 1);

            Mocker.GetMock<IArtistService>().Verify(s => s.FindById(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void reloads_sources_after_a_source_changes()
        {
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith());
            Subject.GetArtistInfo(MusicBrainzId, 1);

            Subject.Handle(new ProviderUpdatedEvent<IMetadataSource>(new MetadataSourceDefinition { Id = 1 }));
            Subject.GetArtistInfo(MusicBrainzId, 1);

            Mocker.GetMock<IMetadataSourceFactory>().Verify(s => s.GetAvailableProviders(), Times.Exactly(2));
        }

        [Test]
        public void and_merge_should_not_add_an_album_the_first_source_lists_by_an_old_id()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith("old-id"));

            var caughtUp = ArtistWith("new-id");
            caughtUp.Albums.Value.Single().OldForeignAlbumIds.Add("old-id");
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(caughtUp);

            Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("old-id");
        }

        [Test]
        public void and_merge_should_not_add_an_album_a_later_source_lists_by_an_id_a_skipped_album_keeps_as_old()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith("old-id"));

            var caughtUp = ArtistWith("new-id");
            caughtUp.Albums.Value.Single().OldForeignAlbumIds.Add("old-id");
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(caughtUp);

            var mergedAgain = ArtistWith("newer-id");
            mergedAgain.Albums.Value.Single().OldForeignAlbumIds.Add("new-id");
            GivenSource(3, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(mergedAgain);

            Subject.GetArtistInfo(MusicBrainzId, 1).Albums.Value.Select(a => a.ForeignAlbumId).Should().Equal("old-id");
        }

        [Test]
        public void and_should_fail_when_a_primary_is_down_even_if_a_fallback_has_the_artist()
        {
            _profile.PrimaryCombination = MetadataSourceCombination.And;
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new TimeoutException("Source 1 is down"));
            GivenSource(2, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Throws(new ArtistNotFoundException(MusicBrainzId));
            var fallback = GivenSource(3, MetadataSourceRole.Fallback);
            fallback.Setup(s => s.GetArtistInfo(MusicBrainzId, 1)).Returns(ArtistWith("album-1"));

            Assert.Throws<TimeoutException>(() => Subject.GetArtistInfo(MusicBrainzId, 1));

            fallback.Verify(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_log_a_failed_source_without_its_url()
        {
            const string token = "s3cr3ttoken";
            var request = new HttpRequest("https://metadata.example.invalid/" + token + "/api/v0.4/artist/" + MusicBrainzId);
            var http = new HttpException(request, new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.InternalServerError));
            GivenSource(1, MetadataSourceRole.Primary).Setup(s => s.GetArtistInfo(MusicBrainzId, 1))
                .Throws(new SkyHookException("Search failed. Unable to communicate with LidarrAPI. {0}", http, http.Message));

            var target = new MemoryTarget("metadata-source-errors") { Layout = "${message} ${exception:format=tostring}" };
            var config = LogManager.Configuration ?? new LoggingConfiguration();
            config.AddRuleForAllLevels(target);
            LogManager.Configuration = config;

            try
            {
                Assert.Throws<SkyHookException>(() => Subject.GetArtistInfo(MusicBrainzId, 1));

                target.Logs.Should().Contain(l => l.Contains("HTTP 500")).And.NotContain(l => l.Contains(token));
            }
            finally
            {
                config.RemoveTarget(target.Name);
                LogManager.Configuration = config;
            }

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
