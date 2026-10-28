using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Profiles.Releases
{
    // The real service and repository over the test database
    [TestFixture]
    public class ReleaseProfileStorageFixture : DbTest<ReleaseProfileService, ReleaseProfile>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IRestrictionRepository>(Mocker.Resolve<ReleaseProfileRepository>());
        }

        private ReleaseProfile Add(params int[] indexerIds)
        {
            return Subject.Add(new ReleaseProfile { Enabled = true, Required = new List<string> { "flac" }, IndexerIds = indexerIds.ToList() });
        }

        private void Execute(string sql)
        {
            using var connection = Mocker.Resolve<IDatabase>().OpenConnection();
            connection.Execute(sql);
        }

        [Test]
        public void should_keep_the_indexer_of_a_profile_an_older_build_added()
        {
            Execute("INSERT INTO \"ReleaseProfiles\" (\"Enabled\", \"Required\", \"Ignored\", \"IndexerId\", \"Tags\") VALUES (1, '[\"flac\"]', '[]', 5, '[]')");

            Subject.All().Single().IndexerIds.Should().Equal(5);
        }

        [Test]
        public void should_take_the_indexer_an_older_build_changed()
        {
            var profile = Add(5, 7);
            Execute($"UPDATE \"ReleaseProfiles\" SET \"IndexerId\" = 9 WHERE \"Id\" = {profile.Id}");

            Subject.Get(profile.Id).IndexerIds.Should().Equal(9);
        }

        [Test]
        public void should_take_every_indexer_when_an_older_build_changed_it_to_any()
        {
            var profile = Add(5, 7);
            Execute($"UPDATE \"ReleaseProfiles\" SET \"IndexerId\" = 0 WHERE \"Id\" = {profile.Id}");

            Subject.Get(profile.Id).IndexerIds.Should().BeEmpty();
        }

        [Test]
        public void should_keep_every_indexer_this_build_saved()
        {
            var profile = Add(5, 7);

            Subject.Get(profile.Id).IndexerIds.Should().Equal(5, 7);
        }

        [Test]
        public void should_take_a_deleted_indexer_out_of_every_profile_and_disable_one_left_with_none()
        {
            var onlyFive = Add(5);
            var fiveAndSeven = Add(5, 7);
            var every = Add();
            var onlyEight = Add(8);

            Subject.Handle(new ProviderDeletedEvent<IIndexer>(5));

            Subject.Get(onlyFive.Id).Should().Match<ReleaseProfile>(p => p.IndexerIds.Count == 0 && !p.Enabled);
            Subject.Get(fiveAndSeven.Id).Should().Match<ReleaseProfile>(p => p.IndexerIds.SequenceEqual(new[] { 7 }) && p.Enabled);
            Subject.Get(every.Id).Should().Match<ReleaseProfile>(p => p.IndexerIds.Count == 0 && p.Enabled);
            Subject.Get(onlyEight.Id).Should().Match<ReleaseProfile>(p => p.IndexerIds.SequenceEqual(new[] { 8 }) && p.Enabled);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_an_edit_saved_while_a_deleted_indexer_is_taken_out()
        {
            Mocker.SetConstant<IRestrictionRepository>(new EditedAfterRead(Mocker.Resolve<IMainDatabase>(), Mocker.Resolve<IEventAggregator>()));
            var onlyFive = Add(5);
            var fiveAndSeven = Add(5, 7);

            Subject.Handle(new ProviderDeletedEvent<IIndexer>(5));

            Subject.Get(onlyFive.Id).Should().Match<ReleaseProfile>(p => p.Required.SequenceEqual(new[] { "edited" }) && p.IndexerIds.Count == 0 && !p.Enabled);
            Subject.Get(fiveAndSeven.Id).Should().Match<ReleaseProfile>(p => p.Required.SequenceEqual(new[] { "edited" }) && p.IndexerIds.SequenceEqual(new[] { 7 }));

            ExceptionVerification.ExpectedWarns(1);
        }

        // Saves an edit to every profile right after a read, as a settings save between the handler's read and write would
        private class EditedAfterRead : ReleaseProfileRepository
        {
            public EditedAfterRead(IMainDatabase database, IEventAggregator eventAggregator)
                : base(database, eventAggregator)
            {
            }

            public override IEnumerable<ReleaseProfile> All()
            {
                var read = base.All().ToList();

                foreach (var profile in base.All())
                {
                    profile.Required = new List<string> { "edited" };
                    Update(profile);
                }

                return read;
            }
        }
    }
}
