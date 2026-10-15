using System.Collections.Generic;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Releases
{
    [TestFixture]
    public class ReleaseProfileRepositoryFixture : DbTest<ReleaseProfileRepository, ReleaseProfile>
    {
        [Test]
        public void should_be_able_to_read_and_write_indexers_and_excluded_tags()
        {
            Subject.Insert(new ReleaseProfile
            {
                Required = new List<string> { "flac" },
                IndexerIds = new List<int> { 5, 7 },
                ExcludedTags = new HashSet<int> { 2 }
            });

            StoredModel.IndexerIds.Should().Equal(5, 7);
            StoredModel.ExcludedTags.Should().BeEquivalentTo(new[] { 2 });
        }

        [Test]
        public void should_store_the_first_indexer_for_older_builds_and_read_back_the_whole_list()
        {
            Subject.Insert(new ReleaseProfile
            {
                Required = new List<string> { "flac" },
                IndexerIds = new List<int> { 5, 7 }
            });

            using var connection = Db.OpenConnection();

            connection.ExecuteScalar<int>("SELECT \"IndexerId\" FROM \"ReleaseProfiles\"").Should().Be(5);
            StoredModel.IndexerIds.Should().Equal(5, 7);
        }
    }
}
