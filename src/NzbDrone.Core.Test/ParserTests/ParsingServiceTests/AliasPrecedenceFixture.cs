using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // The real parsing and artist services over the test database
    [TestFixture]
    public class AliasPrecedenceFixture : DbTest<ParsingService, Artist>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IArtistRepository>(Mocker.Resolve<ArtistRepository>());
            Mocker.SetConstant<IArtistService>(Mocker.Resolve<ArtistService>());
        }

        private void AddArtist(string name, List<string> aliases)
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
        }

        [Test]
        public void should_map_a_release_title_without_the_artists_leading_the_to_that_artist()
        {
            AddArtist("The Zeta Orchestra", new List<string>());
            AddArtist("Keys Tribute", new List<string> { "Zeta Orchestra" });

            Subject.GetArtist("Zeta Orchestra - Northern Lights (2020) [FLAC]").Name.Should().Be("The Zeta Orchestra");
        }
    }
}
