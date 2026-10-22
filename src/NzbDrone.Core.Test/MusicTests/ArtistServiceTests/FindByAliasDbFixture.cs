using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistServiceTests
{
    [TestFixture]
    public class FindByAliasDbFixture : DbTest<ArtistService, Artist>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
        }

        private Artist AddArtist(string name, List<string> aliases)
        {
            var metadata = Builder<ArtistMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Name = name)
                .With(a => a.ForeignArtistId = name)
                .With(a => a.Aliases = aliases)
                .BuildNew();

            Mocker.Resolve<ArtistMetadataRepository>().Insert(metadata);

            var artist = Builder<Artist>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Metadata = metadata)
                .With(a => a.ArtistMetadataId = metadata.Id)
                .With(a => a.CleanName = Parser.Parser.CleanArtistName(name))
                .BuildNew();

            Mocker.Resolve<ArtistRepository>().Insert(artist);

            return artist;
        }

        [Test]
        public void should_match_an_artist_named_without_its_leading_the_before_another_artists_alias()
        {
            AddArtist("The Zeta Orchestra", new List<string>());
            AddArtist("Keys Tribute", new List<string> { "Zeta Orchestra" });

            Subject.FindByNameInexact("Zeta Orchestra").Name.Should().Be("The Zeta Orchestra");
        }

        [Test]
        public void should_match_a_short_artist_named_without_its_leading_the_before_another_artists_alias()
        {
            AddArtist("The AB", new List<string>());
            AddArtist("Other Artist", new List<string> { "AB" });

            Subject.FindByNameInexact("AB").Name.Should().Be("The AB");
        }

        [Test]
        public void should_leave_two_artists_named_like_the_title_to_the_fuzzy_matching()
        {
            var artists = new List<Artist>
            {
                AddArtist("The AB", new List<string>()),
                AddArtist("AB", new List<string>()),
                AddArtist("Other Artist", new List<string> { "AB" })
            };

            ArtistAliasMatcher.FindByNameOrAlias(artists, "AB").Should().BeNull();
        }

        [Test]
        public void should_match_an_artist_named_without_its_leading_the_without_aliases()
        {
            AddArtist("The Zeta Orchestra", new List<string>());
            AddArtist("Keys Tribute", new List<string>());

            Subject.FindByNameInexact("Zeta Orchestra").Name.Should().Be("The Zeta Orchestra");
        }

        [TestCase("Peas Collective", "Peas Collective")]
        [TestCase("PEAS COLLECTIVE", "Peas Collective")]
        [TestCase("Peas & Collective", "Peas and Collective")]
        [TestCase("Péas Collective", "Peas Collective")]
        [TestCase("Кино", "КИНО")]
        public void should_match_an_alias_whatever_its_case_accents_or_ampersand(string title, string alias)
        {
            AddArtist("Will I Am Group", new List<string> { alias });
            AddArtist("Other Artist", new List<string>());

            Subject.FindByNameInexact(title).Name.Should().Be("Will I Am Group");
        }

        [TestCase("!!!")]
        [TestCase("   ")]
        public void should_not_match_an_alias_that_cleans_to_nothing(string title)
        {
            AddArtist("Will I Am Group", new List<string> { "!!!", "" });
            AddArtist("Other Artist", new List<string>());

            Subject.FindByNameInexact(title).Should().BeNull();
        }

        [Test]
        public void should_match_an_artist_whose_aliases_repeat()
        {
            AddArtist("Will I Am Group", new List<string> { "Peas Collective", "peas collective", "Peas-Collective" });
            AddArtist("Other Artist", new List<string>());

            Subject.FindByNameInexact("Peas Collective").Name.Should().Be("Will I Am Group");
        }

        [Test]
        public void should_match_an_artist_named_with_a_leading_the_before_another_artists_alias()
        {
            AddArtist("Zeta Orchestra", new List<string>());
            AddArtist("Keys Tribute", new List<string> { "The Zeta Orchestra" });

            Subject.FindByNameInexact("The Zeta Orchestra").Name.Should().Be("Zeta Orchestra");
        }

        [Test]
        public void should_match_an_alias_added_since_the_artist_list_was_read()
        {
            var artist = AddArtist("Will I Am Group", new List<string>());

            // One letter off the alias, so only the exact alias check picks the right artist
            AddArtist("Peas Collectives", new List<string>());

            Subject.FindByNameInexact("Peas Collective").Name.Should().Be("Peas Collectives");

            artist.Metadata.Value.Aliases = new List<string> { "Peas Collective" };
            Mocker.Resolve<ArtistMetadataRepository>().Update(artist.Metadata.Value);
            Subject.UpdateArtist(artist);

            Subject.FindByNameInexact("Peas Collective").Name.Should().Be("Will I Am Group");
        }
    }
}
