using System.Collections.Generic;
using FluentAssertions;
using Lidarr.Api.V1.Tags;
using NUnit.Framework;
using NzbDrone.Core.Tags;

namespace NzbDrone.Api.Test.Tags
{
    [TestFixture]
    public class TagDetailsResourceFixture
    {
        [Test]
        public void root_folders_using_the_tag_should_reach_the_resource()
        {
            var details = new TagDetails
            {
                Id = 3,
                Label = "Tag Label",
                ArtistIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                ImportListIds = new List<int>(),
                RootFolderIds = new List<int> { 7 },
                IndexerIds = new List<int>(),
                AutoTagIds = new List<int>(),
                DownloadClientIds = new List<int>()
            };

            details.InUse.Should().BeTrue();
            details.ToResource().RootFolderIds.Should().Equal(7);
        }
    }
}
