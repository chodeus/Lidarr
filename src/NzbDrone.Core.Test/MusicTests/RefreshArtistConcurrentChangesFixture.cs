using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // The real ArtistService over the test database; the lookup, the album refresh and auto-tagging are mocked
    [TestFixture]
    public class RefreshArtistConcurrentChangesFixture : DbTest<RefreshArtistService, Artist>
    {
        private ArtistMetadata _metadata;
        private Artist _artist;
        private ArtistService _artistService;
        private List<ArtistUpdatedEvent> _updated;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            _artistService = Mocker.Resolve<ArtistService>();
            Mocker.SetConstant<IArtistService>(_artistService);
            Mocker.SetConstant<IAlbumRepository>(Mocker.Resolve<AlbumRepository>());
            Mocker.SetConstant<IAlbumService>(Mocker.Resolve<AlbumService>());

            _metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = "artist-1").With(m => m.Status = ArtistStatusType.Continuing).Build();
            Db.Insert(_metadata);

            _artist = Builder<Artist>.CreateNew()
                                     .With(a => a.Id = 0)
                                     .With(a => a.ArtistMetadataId = _metadata.Id)
                                     .With(a => a.CleanName = "artistname")
                                     .With(a => a.SortName = "artist name")
                                     .With(a => a.Path = "/music/Artist Name".AsOsAgnostic())
                                     .With(a => a.RootFolderPath = "/music".AsOsAgnostic())
                                     .With(a => a.Monitored = true)
                                     .With(a => a.MonitorNewItems = NewItemMonitorTypes.All)
                                     .With(a => a.QualityProfileId = 1)
                                     .With(a => a.MetadataProfileId = 1)
                                     .With(a => a.Tags = new HashSet<int>())
                                     .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                     .Build();
            Db.Insert(_artist);

            Mocker.GetMock<IArtistMetadataService>().Setup(s => s.UpsertMany(It.IsAny<List<ArtistMetadata>>())).Returns(true);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByForeignId(It.IsAny<List<string>>())).Returns(new List<ImportListExclusion>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.GetTagChanges(It.IsAny<Artist>())).Returns(new AutoTaggingChanges());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByArtist(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>())).Returns(new List<EntityHistory>());

            _updated = new List<ArtistUpdatedEvent>();
            Mocker.GetMock<IEventAggregator>().Setup(s => s.PublishEvent(It.IsAny<ArtistUpdatedEvent>())).Callback<ArtistUpdatedEvent>(e => _updated.Add(e));

            GivenLookup(() => { });
        }

        // What the metadata source returns: new names, the same metadata row
        private void GivenLookup(Action duringLookup)
        {
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Returns(() =>
                  {
                      duringLookup();

                      var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = _metadata.Id).With(m => m.ForeignArtistId = "artist-1").With(m => m.Name = "Artist Name Renamed").Build();

                      return new Artist
                      {
                          CleanName = "artistnamerenamed",
                          SortName = "artist name renamed",
                          Metadata = metadata,
                          Albums = new List<Album>()
                      };
                  });
        }

        private void EditArtist()
        {
            var artist = _artistService.GetArtist(_artist.Id);
            artist.Monitored = false;
            artist.MonitorNewItems = NewItemMonitorTypes.None;
            artist.QualityProfileId = 2;
            artist.MetadataProfileId = 3;
            artist.Tags = new HashSet<int> { 7 };
            artist.Path = "/other/Artist Name".AsOsAgnostic();
            _artistService.UpdateArtist(artist);
        }

        private void ShouldKeepTheEditAndTheRefresh(Artist stored)
        {
            new
            {
                stored.Monitored,
                stored.MonitorNewItems,
                stored.QualityProfileId,
                stored.MetadataProfileId,
                Tags = stored.Tags.ToList(),
                stored.Path,
                stored.CleanName,
                stored.SortName
            }.Should().BeEquivalentTo(new
            {
                Monitored = false,
                MonitorNewItems = NewItemMonitorTypes.None,
                QualityProfileId = 2,
                MetadataProfileId = 3,
                Tags = new List<int> { 7 },
                Path = "/other/Artist Name".AsOsAgnostic(),
                CleanName = "artistnamerenamed",
                SortName = "artist name renamed"
            });

            stored.LastInfoSync.Should().BeAfter(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_keep_an_edit_saved_during_the_lookup()
        {
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ShouldKeepTheEditAndTheRefresh(_artistService.GetArtist(_artist.Id));
        }

        [Test]
        public void should_keep_an_edit_saved_while_the_albums_refresh()
        {
            Mocker.GetMock<IRefreshAlbumService>()
                  .Setup(s => s.RefreshAlbumInfo(It.IsAny<List<Album>>(), It.IsAny<List<Album>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<DateTime?>()))
                  .Callback(EditArtist)
                  .Returns(false);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ShouldKeepTheEditAndTheRefresh(_artistService.GetArtist(_artist.Id));
        }

        [Test]
        public void should_keep_an_edit_saved_during_a_full_refresh_that_read_every_artist_first()
        {
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand { Trigger = CommandTrigger.Manual });

            ShouldKeepTheEditAndTheRefresh(_artistService.GetArtist(_artist.Id));
        }

        [Test]
        public void should_announce_the_stored_artist()
        {
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            _updated.Should().NotBeEmpty();
            _updated.Should().OnlyContain(e => !e.Artist.Monitored && e.Artist.Path == "/other/Artist Name".AsOsAgnostic());
        }

        [Test]
        public void should_keep_a_move_and_still_tidy_a_path_nothing_changed()
        {
            var untidy = _artistService.GetArtist(_artist.Id);
            untidy.Path = "/music/./Artist Name".AsOsAgnostic();
            _artistService.UpdateArtist(untidy, false);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            _artistService.GetArtist(_artist.Id).Path.Should().Be("/music/Artist Name".AsOsAgnostic());
        }

        [Test]
        public void should_keep_an_edit_saved_when_the_lookup_finds_no_artist()
        {
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Callback(EditArtist)
                  .Throws(new ArtistNotFoundException("artist-1"));

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            _artistService.GetArtist(_artist.Id).Monitored.Should().BeFalse();
        }

        [Test]
        public void should_keep_a_move_saved_during_the_lookup_of_an_artist_whose_path_needed_tidying()
        {
            var untidy = _artistService.GetArtist(_artist.Id);
            untidy.Path = "/music/./Artist Name".AsOsAgnostic();
            _artistService.UpdateArtist(untidy, false);
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            _artistService.GetArtist(_artist.Id).Path.Should().Be("/other/Artist Name".AsOsAgnostic());
        }

        [Test]
        public void should_keep_an_edit_when_auto_tagging_changes_the_tags()
        {
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.GetTagChanges(It.IsAny<Artist>())).Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 9 } });
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            var stored = _artistService.GetArtist(_artist.Id);
            new { stored.Monitored, Tags = stored.Tags.OrderBy(t => t).ToList() }.Should().BeEquivalentTo(new { Monitored = false, Tags = new List<int> { 7, 9 } });
        }

        [Test]
        public void should_rescan_a_new_artist_at_the_path_saved_during_its_refresh()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Always);
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }, true));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.Is<RescanFoldersCommand>(c => c.Folders.Single() == "/other/Artist Name".AsOsAgnostic()), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_not_announce_an_artist_deleted_during_its_refresh()
        {
            var completed = new List<ArtistRefreshCompleteEvent>();
            Mocker.GetMock<IEventAggregator>().Setup(s => s.PublishEvent(It.IsAny<ArtistRefreshCompleteEvent>())).Callback<ArtistRefreshCompleteEvent>(e => completed.Add(e));
            GivenLookup(() => _artistService.DeleteArtist(_artist.Id, false));

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            new { Updated = _updated.Count, Completed = completed.Count }.Should().BeEquivalentTo(new { Updated = 0, Completed = 0 });
        }

        [Test]
        public void should_announce_the_stored_artist_when_the_lookup_finds_no_artist()
        {
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Callback(EditArtist)
                  .Throws(new ArtistNotFoundException("artist-1"));

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            _updated.Should().ContainSingle().Which.Artist.Should().Match<Artist>(a => !a.Monitored && a.Metadata.Value.Status == ArtistStatusType.Deleted);
        }

        // The real auto-tagging service over a "Status: Deleted" rule; the lookup's status is never stored
        [Test]
        public void should_auto_tag_an_artist_the_lookup_marks_deleted()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IAutoTaggingRepository>().Setup(s => s.All()).Returns(new List<AutoTag>
            {
                new AutoTag
                {
                    Name = "Auto Tag Name",
                    Specifications = new List<IAutoTaggingSpecification> { new StatusSpecification { Name = "Status", Status = (int)ArtistStatusType.Deleted } },
                    Tags = new HashSet<int> { 5 }
                }
            });
            Mocker.SetConstant<IAutoTaggingService>(Mocker.Resolve<AutoTaggingService>());
            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(It.IsAny<string>(), It.IsAny<int>()))
                  .Throws(new ArtistNotFoundException("artist-1"));

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            ExceptionVerification.ExpectedErrors(2);
            _artistService.GetArtist(_artist.Id).Tags.Should().Contain(5);
        }

        private Artist GivenAnotherArtist(string foreignArtistId)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew().With(m => m.Id = 0).With(m => m.ForeignArtistId = foreignArtistId).Build();
            Db.Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                                        .With(a => a.Id = 0)
                                        .With(a => a.ArtistMetadataId = metadata.Id)
                                        .With(a => a.Path = ("/music/" + foreignArtistId).AsOsAgnostic())
                                        .With(a => a.Tags = new HashSet<int>())
                                        .With(a => a.LastInfoSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                                        .Build();
            Db.Insert(artist);
            artist.Metadata = metadata;

            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo(foreignArtistId, It.IsAny<int>()))
                  .Returns(() => new Artist { CleanName = foreignArtistId, SortName = foreignArtistId, Metadata = metadata, Albums = new List<Album>() });

            return artist;
        }

        private void GivenTheFirstAlbumRefreshFails()
        {
            var refreshes = 0;
            Mocker.GetMock<IRefreshAlbumService>()
                  .Setup(s => s.RefreshAlbumInfo(It.IsAny<List<Album>>(), It.IsAny<List<Album>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<DateTime?>()))
                  .Returns(() => refreshes++ == 0 ? throw new HttpException(new HttpResponse(new HttpRequest("https://example.test/"), new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.ServiceUnavailable)) : false);
        }

        private List<int> RescannedArtistIds(List<int> artistIds)
        {
            var rescans = new List<RescanFoldersCommand>();
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(RescanAfterRefreshType.Always);
            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<RescanFoldersCommand, CommandPriority, CommandTrigger>((c, p, t) => rescans.Add(c));

            Subject.Execute(new RefreshArtistCommand(artistIds) { Trigger = CommandTrigger.Manual });

            return rescans.Single().ArtistIds;
        }

        private void GivenAutoTaggingDeletesTheArtist()
        {
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.Is<Artist>(a => a.Id == _artist.Id)))
                  .Callback(() => _artistService.DeleteArtist(_artist.Id, false))
                  .Returns(new AutoTaggingChanges());
        }

        // The merge deletes the refreshed artist, then its album refresh fails; the next artist must still refresh
        [Test]
        public void should_refresh_the_rest_of_the_batch_when_a_merged_artist_then_fails()
        {
            var target = GivenAnotherArtist("artist-target");
            var other = GivenAnotherArtist("artist-other");

            Mocker.GetMock<IProvideArtistInfo>()
                  .Setup(s => s.GetArtistInfo("artist-1", It.IsAny<int>()))
                  .Returns(() => new Artist { CleanName = "artistnamerenamed", SortName = "artist name renamed", Metadata = target.Metadata.Value, Albums = new List<Album>() });
            GivenTheFirstAlbumRefreshFails();

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id, other.Id }));

            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
            _artistService.GetArtist(other.Id).LastInfoSync.Should().BeAfter(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_leave_an_artist_deleted_while_auto_tagging_decides_out_of_the_rescan()
        {
            var other = GivenAnotherArtist("artist-other");
            GivenAutoTaggingDeletesTheArtist();

            RescannedArtistIds(new List<int> { _artist.Id, other.Id }).Should().Equal(other.Id);
        }

        [Test]
        public void should_leave_a_failed_artist_deleted_while_auto_tagging_decides_out_of_the_rescan()
        {
            var other = GivenAnotherArtist("artist-other");
            GivenTheFirstAlbumRefreshFails();
            GivenAutoTaggingDeletesTheArtist();

            RescannedArtistIds(new List<int> { _artist.Id, other.Id }).Should().Equal(other.Id);
            ExceptionVerification.ExpectedErrors(1);
        }

        // The real auto-tagging service over a rule for unmonitored artists; the edit that unmonitors comes during the lookup
        [Test]
        public void should_tag_by_rules_against_the_artist_as_stored_after_its_refresh()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IAutoTaggingRepository>().Setup(s => s.All()).Returns(new List<AutoTag>
            {
                new AutoTag
                {
                    Name = "Auto Tag Name",
                    Specifications = new List<IAutoTaggingSpecification> { new MonitoredSpecification { Name = "Monitored", Negate = true } },
                    Tags = new HashSet<int> { 5 }
                }
            });
            Mocker.SetConstant<IAutoTaggingService>(Mocker.Resolve<AutoTaggingService>());
            GivenLookup(EditArtist);

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            _artistService.GetArtist(_artist.Id).Tags.Should().Contain(5);
        }

        [Test]
        public void should_keep_an_edit_saved_while_auto_tagging_decides()
        {
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.IsAny<Artist>()))
                  .Callback(EditArtist)
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 9 } });

            Subject.Execute(new RefreshArtistCommand(new List<int> { _artist.Id }));

            var stored = _artistService.GetArtist(_artist.Id);
            new { stored.Monitored, Tags = stored.Tags.OrderBy(t => t).ToList() }.Should().BeEquivalentTo(new { Monitored = false, Tags = new List<int> { 7, 9 } });
        }

        // Fails by name for a field the refresh sets but doesn't save, such as one added upstream
        [Test]
        public void should_save_every_field_the_refresh_sets()
        {
            var refreshed = _artistService.GetArtist(_artist.Id);

            Subject.RefreshEntityInfo(refreshed, null, true, false, null);

            RefreshedFields.ShouldMatch(refreshed, _artistService.GetArtist(_artist.Id), "Monitored", "MonitorNewItems", "Path", "QualityProfileId", "MetadataProfileId", "Tags", "Added", "AddOptions");
        }
    }
}
