using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class CachedIndexerSettingsProviderFixture : CoreTest<CachedIndexerSettingsProvider>
    {
        private class SettingsWithoutFailDownloads : IIndexerSettings
        {
            public string BaseUrl { get; set; }
            public int? EarlyReleaseLimit { get; set; }

            public NzbDroneValidationResult Validate()
            {
                return new NzbDroneValidationResult();
            }
        }

        private class SettingsWithFailDownloads : SettingsWithoutFailDownloads, IIndexerSettings
        {
            public IEnumerable<int> FailDownloads { get; set; } = new[] { (int)Indexers.FailDownloads.PotentiallyDangerous };
        }

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());
        }

        private void GivenSettings(IIndexerSettings settings)
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.Find(4))
                  .Returns(new IndexerDefinition { Id = 4, Settings = settings });
        }

        [Test]
        public void should_read_fail_downloads_from_indexer_settings()
        {
            GivenSettings(new SettingsWithFailDownloads());

            Subject.GetSettings(4).FailDownloads.Should().BeEquivalentTo(new[] { FailDownloads.PotentiallyDangerous });
        }

        [Test]
        public void should_treat_settings_without_fail_downloads_as_none()
        {
            GivenSettings(new SettingsWithoutFailDownloads());

            Subject.GetSettings(4).FailDownloads.Should().BeEmpty();
        }

        [Test]
        public void should_read_new_settings_after_the_indexer_is_updated()
        {
            GivenSettings(new SettingsWithFailDownloads());
            Subject.GetSettings(4);

            GivenSettings(new SettingsWithoutFailDownloads());
            Subject.Handle(new ProviderUpdatedEvent<IIndexer>(new IndexerDefinition { Id = 4 }));

            Subject.GetSettings(4).FailDownloads.Should().BeEmpty();
        }

        [Test]
        public void should_forget_settings_after_the_indexer_is_deleted()
        {
            GivenSettings(new SettingsWithFailDownloads());
            Subject.GetSettings(4);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.Find(4))
                  .Returns((IndexerDefinition)null);
            Subject.Handle(new ProviderDeletedEvent<IIndexer>(4));

            Subject.GetSettings(4).Should().BeNull();
        }

        [Test]
        public void should_return_null_for_no_indexer()
        {
            Subject.GetSettings(0).Should().BeNull();
        }
    }
}
