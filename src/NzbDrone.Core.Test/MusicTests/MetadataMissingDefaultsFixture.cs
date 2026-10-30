using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class MetadataMissingDefaultsFixture
    {
        [TestCase(typeof(IAlbumService), nameof(IAlbumService.UpdateMetadataMissingSince))]
        [TestCase(typeof(IAlbumService), nameof(IAlbumService.GetAlbumsMissingFromMetadataWithoutFiles))]
        [TestCase(typeof(IAlbumRepository), nameof(IAlbumRepository.GetAlbumsMissingFromMetadataWithoutFiles))]
        public void should_give_new_interface_members_a_default_body(Type type, string method)
        {
            type.GetMethod(method).IsAbstract.Should().BeFalse();
        }
    }
}
