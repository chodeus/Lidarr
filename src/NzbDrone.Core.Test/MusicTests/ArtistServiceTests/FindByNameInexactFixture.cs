using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistServiceTests
{
    [TestFixture]

    public class FindByNameInexactFixture : CoreTest<ArtistService>
    {
        private List<Artist> _artists;

        private Artist CreateArtist(string name)
        {
            return Builder<Artist>.CreateNew()
                .With(a => a.Name = name)
                .With(a => a.CleanName = Parser.Parser.CleanArtistName(name))
                .With(a => a.ForeignArtistId = name)
                .BuildNew();
        }

        [SetUp]
        public void Setup()
        {
            _artists = new List<Artist>();
            _artists.Add(CreateArtist("The Black Eyed Peas"));
            _artists.Add(CreateArtist("The Black Keys"));

            Mocker.GetMock<IArtistRepository>()
                .Setup(s => s.All())
                .Returns(_artists);
        }

        [TestCase("The Black Eyde Peas", "The Black Eyed Peas")]
        [TestCase("Black Eyed Peas", "The Black Eyed Peas")]
        [TestCase("The Black eys", "The Black Keys")]
        [TestCase("Black Keys", "The Black Keys")]
        public void should_find_artist_in_db_by_name_inexact(string name, string expected)
        {
            var artist = Subject.FindByNameInexact(name);

            artist.Should().NotBeNull();
            artist.Name.Should().Be(expected);
        }

        [Test]
        public void should_find_artist_when_the_is_omitted_from_start()
        {
            _artists = new List<Artist>();
            _artists.Add(CreateArtist("Black Keys"));
            _artists.Add(CreateArtist("The Black Eyed Peas"));

            Mocker.GetMock<IArtistRepository>()
                .Setup(s => s.All())
                .Returns(_artists);

            Subject.FindByNameInexact("The Black Keys").Should().NotBeNull();
        }

        [TestCase("The Black Peas")]
        public void should_not_find_artist_in_db_by_ambiguous_name(string name)
        {
            var artist = Subject.FindByNameInexact(name);

            artist.Should().BeNull();
        }

        private Artist WithAliases(string name, params string[] aliases)
        {
            var artist = CreateArtist(name);
            artist.Metadata.Value.Aliases = aliases.ToList();
            _artists.Add(artist);

            return artist;
        }

        [TestCase("Peas Collective")]
        [TestCase("peas collective")]
        public void should_find_artist_by_exact_alias(string name)
        {
            WithAliases("Will I Am Group", "Peas Collective");

            Subject.FindByNameInexact(name).Name.Should().Be("Will I Am Group");
        }

        [Test]
        public void should_prefer_an_artist_name_over_another_artists_alias()
        {
            WithAliases("Keys Tribute", "Zeta Orchestra");
            var named = CreateArtist("Zeta Orchestra");
            _artists.Add(named);

            Subject.FindByNameInexact("Zeta Orchestra").Should().Be(named);
        }

        [Test]
        public void should_not_match_an_alias_shared_by_several_artists()
        {
            WithAliases("First Artist", "Shared Alias");
            WithAliases("Second Artist", "Shared Alias");

            Subject.FindByNameInexact("Shared Alias").Should().BeNull();
        }

        [Test]
        public void should_match_alias_before_a_fuzzy_name_match()
        {
            WithAliases("Keys Side Project", "The Black Keyss");

            Subject.FindByNameInexact("The Black Keyss").Name.Should().Be("Keys Side Project");
        }
    }
}
