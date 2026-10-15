using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_release_profile_indexer_idsFixture : MigrationTest<add_release_profile_indexer_ids>
    {
        private static void AddReleaseProfile(add_release_profile_indexer_ids c, int indexerId)
        {
            c.Insert.IntoTable("ReleaseProfiles").Row(new
            {
                Enabled = true,
                Required = "[\"flac\"]",
                Ignored = "[]",
                IndexerId = indexerId,
                Tags = "[]"
            });
        }

        [Test]
        public void should_convert_any_indexer_to_an_empty_list()
        {
            var db = WithMigrationTestDb(c => AddReleaseProfile(c, 0));

            var releaseProfile = db.Query<ReleaseProfile912>("SELECT \"Id\", \"IndexerId\", \"IndexerIds\" FROM \"ReleaseProfiles\"").Single();

            Json.Deserialize<List<int>>(releaseProfile.IndexerIds).Should().BeEmpty();
        }

        [Test]
        public void should_convert_a_single_indexer_to_a_list()
        {
            var db = WithMigrationTestDb(c => AddReleaseProfile(c, 42));

            var releaseProfile = db.Query<ReleaseProfile912>("SELECT \"Id\", \"IndexerId\", \"IndexerIds\" FROM \"ReleaseProfiles\"").Single();

            Json.Deserialize<List<int>>(releaseProfile.IndexerIds).Should().Equal(42);
            releaseProfile.IndexerId.Should().Be(42);
        }
    }

    internal class ReleaseProfile912
    {
        public int Id { get; set; }
        public int IndexerId { get; set; }
        public string IndexerIds { get; set; }
    }
}
