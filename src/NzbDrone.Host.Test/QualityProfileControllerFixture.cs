using System.Collections.Generic;
using Lidarr.Api.V1.Profiles.Quality;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class QualityProfileControllerFixture : TestBase<QualityProfileController>
    {
        [SetUp]
        public void Setup()
        {
            var stored = new QualityProfile { Id = 1, MinUpgradeFormatScore = 30, Items = new List<QualityProfileQualityItem>(), FormatItems = new List<ProfileFormatItem>() };

            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Get(1)).Returns(stored);
            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Add(It.IsAny<QualityProfile>())).Returns(stored);
        }

        private static QualityProfileResource Resource(int? minUpgradeFormatScore)
        {
            return new QualityProfileResource { Id = 1, Items = new List<QualityProfileQualityItemResource>(), FormatItems = new List<ProfileFormatItemResource>(), MinUpgradeFormatScore = minUpgradeFormatScore };
        }

        [Test]
        public void should_keep_the_stored_increment_when_a_save_leaves_it_out()
        {
            Subject.Update(Resource(null));

            Mocker.GetMock<IQualityProfileService>().Verify(s => s.Update(It.Is<QualityProfile>(p => p.MinUpgradeFormatScore == 30)), Times.Once());
        }

        [Test]
        public void should_save_the_increment_a_save_sends()
        {
            Subject.Update(Resource(5));

            Mocker.GetMock<IQualityProfileService>().Verify(s => s.Update(It.Is<QualityProfile>(p => p.MinUpgradeFormatScore == 5)), Times.Once());
        }

        [Test]
        public void should_give_a_new_profile_that_leaves_the_increment_out_an_increment_of_1()
        {
            Subject.Create(Resource(null));

            Mocker.GetMock<IQualityProfileService>().Verify(s => s.Add(It.Is<QualityProfile>(p => p.MinUpgradeFormatScore == 1)), Times.Once());
        }
    }
}
