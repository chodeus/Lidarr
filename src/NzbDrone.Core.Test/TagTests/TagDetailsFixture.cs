using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tags;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TagTests
{
    [TestFixture]
    public class TagDetailsFixture : TestBase
    {
        [Test]
        public void should_not_count_a_tag_in_use_when_only_develops_lists_are_set()
        {
            var details = new TagDetails
            {
                ArtistIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                ImportListIds = new List<int>(),
                RootFolderIds = new List<int>(),
                IndexerIds = new List<int>(),
                AutoTagIds = new List<int>(),
                DownloadClientIds = new List<int>()
            };

            details.InUse.Should().BeFalse();
        }
    }
}
