using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistServiceTests
{
    [TestFixture]
    public class UpdateArtistTagsFixture : DbTest<ArtistService, Artist>
    {
        private Artist _artist;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            var metadata = Builder<ArtistMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .BuildNew();
            Db.Insert(metadata);

            _artist = Builder<Artist>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ArtistMetadataId = metadata.Id)
                .With(a => a.Monitored = true)
                .With(a => a.Tags = new HashSet<int> { 1 })
                .BuildNew();
            Db.Insert(_artist);
        }

        [Test]
        public void should_write_only_the_tags()
        {
            var artist = Subject.GetArtist(_artist.Id);
            artist.Tags.Add(2);
            artist.Monitored = false;

            Subject.UpdateTags(new List<Artist> { artist });

            var stored = Subject.GetArtist(_artist.Id);
            stored.Tags.Should().BeEquivalentTo(new[] { 1, 2 });
            stored.Monitored.Should().BeTrue();
        }

        [Test]
        public void should_show_the_new_tags_in_the_artist_list()
        {
            Subject.GetAllArtists();

            var artist = Subject.GetArtist(_artist.Id);
            artist.Tags.Add(2);
            Subject.UpdateTags(new List<Artist> { artist });

            Subject.GetAllArtists().Should().ContainSingle(a => a.Tags.SetEquals(new[] { 1, 2 }));
        }

        [Test]
        public void should_give_the_tag_update_added_to_the_develop_artist_service_a_default()
        {
            typeof(IArtistService).GetMethod("UpdateTags").IsAbstract.Should().BeFalse();
        }
    }
}
