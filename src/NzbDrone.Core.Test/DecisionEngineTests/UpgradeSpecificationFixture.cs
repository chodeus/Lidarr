using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class UpgradeSpecificationFixture : CoreTest<UpgradableSpecification>
    {
        public static object[] IsUpgradeTestCases =
        {
            new object[] { Quality.MP3_192, 1, Quality.MP3_192, 2, Quality.MP3_192, true },
            new object[] { Quality.MP3_320, 1, Quality.MP3_320, 2, Quality.MP3_320, true },
            new object[] { Quality.MP3_192, 1, Quality.MP3_192, 1, Quality.MP3_192, false },
            new object[] { Quality.MP3_320, 1, Quality.MP3_256, 2, Quality.MP3_320, false },
            new object[] { Quality.MP3_320, 1, Quality.MP3_256, 2, Quality.MP3_320, false },
            new object[] { Quality.MP3_320, 1, Quality.MP3_320, 1, Quality.MP3_320, false }
        };

        private readonly CustomFormat _formatTen = new CustomFormat { Id = 1, Name = "Ten" };
        private readonly CustomFormat _formatTwenty = new CustomFormat { Id = 2, Name = "Twenty" };

        private void GivenAutoDownloadPropers(ProperDownloadTypes type)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.DownloadPropersAndRepacks)
                  .Returns(type);
        }

        [Test]
        [TestCaseSource(nameof(IsUpgradeTestCases))]
        public void IsUpgradeTest(Quality current, int currentVersion, Quality newQuality, int newVersion, Quality cutoff, bool expected)
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.PreferAndUpgrade);

            var profile = new QualityProfile
            {
                UpgradeAllowed = true,
                Items = Qualities.QualityFixture.GetDefaultQualities()
            };

            Subject.IsUpgradable(
                        profile,
                        new List<QualityModel> { new QualityModel(current, new Revision(version: currentVersion)) },
                        new List<CustomFormat>(),
                        new QualityModel(newQuality, new Revision(version: newVersion)),
                        new List<CustomFormat>())
                   .Should().Be(expected);
        }

        [Test]
        public void should_return_true_if_proper_and_download_propers_is_do_not_download()
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.DoNotUpgrade);

            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
            };

            Subject.IsUpgradable(
                        profile,
                        new List<QualityModel> { new QualityModel(Quality.MP3_256, new Revision(version: 1)) },
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3_256, new Revision(version: 2)),
                        new List<CustomFormat>())
                    .Should().BeTrue();
        }

        private QualityProfile GivenProfile(Quality cutoff, int cutoffFormatScore = 0, int minUpgradeFormatScore = 1)
        {
            return new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = cutoff.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                CutoffFormatScore = cutoffFormatScore,
                MinUpgradeFormatScore = minUpgradeFormatScore,
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = _formatTen, Score = 10 },
                    new ProfileFormatItem { Format = _formatTwenty, Score = 20 }
                }
            };
        }

        [Test]
        public void should_reject_when_new_release_is_a_downgrade_for_any_file()
        {
            Subject.GetUpgradeRejectReason(
                        GivenProfile(Quality.FLAC),
                        new List<QualityModel> { new QualityModel(Quality.MP3_192), new QualityModel(Quality.MP3_320) },
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3_256),
                        new List<CustomFormat>())
                   .Should().Be(UpgradeableRejectReason.BetterQuality);
        }

        [Test]
        public void should_accept_when_one_file_is_upgraded_and_none_downgraded()
        {
            Subject.GetUpgradeRejectReason(
                        GivenProfile(Quality.FLAC),
                        new List<QualityModel> { new QualityModel(Quality.MP3_192), new QualityModel(Quality.MP3_320) },
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3_320),
                        new List<CustomFormat>())
                   .Should().Be(UpgradeableRejectReason.None);
        }

        [Test]
        public void should_reject_higher_quality_when_quality_cutoff_is_already_met()
        {
            Subject.GetUpgradeRejectReason(
                        GivenProfile(Quality.MP3_320, cutoffFormatScore: 100),
                        new List<QualityModel> { new QualityModel(Quality.MP3_320) },
                        new List<CustomFormat>(),
                        new QualityModel(Quality.FLAC),
                        new List<CustomFormat>())
                   .Should().Be(UpgradeableRejectReason.QualityCutoff);
        }

        [Test]
        public void should_reject_custom_format_upgrade_when_custom_format_cutoff_is_already_met()
        {
            Subject.GetUpgradeRejectReason(
                        GivenProfile(Quality.FLAC, cutoffFormatScore: 10),
                        new List<QualityModel> { new QualityModel(Quality.MP3_320) },
                        new List<CustomFormat> { _formatTen },
                        new QualityModel(Quality.MP3_320),
                        new List<CustomFormat> { _formatTwenty })
                   .Should().Be(UpgradeableRejectReason.CustomFormatCutoff);
        }

        [TestCase(11, UpgradeableRejectReason.MinCustomFormatScore)]
        [TestCase(10, UpgradeableRejectReason.None)]
        public void should_require_the_minimum_custom_format_score_increment(int minUpgradeFormatScore, UpgradeableRejectReason expected)
        {
            Subject.GetUpgradeRejectReason(
                        GivenProfile(Quality.FLAC, cutoffFormatScore: 100, minUpgradeFormatScore: minUpgradeFormatScore),
                        new List<QualityModel> { new QualityModel(Quality.MP3_320) },
                        new List<CustomFormat> { _formatTen },
                        new QualityModel(Quality.MP3_320),
                        new List<CustomFormat> { _formatTwenty })
                   .Should().Be(expected);
        }

        [Test]
        public void should_return_false_if_proper_and_autoDownloadPropers_is_do_not_prefer()
        {
            GivenAutoDownloadPropers(ProperDownloadTypes.DoNotPrefer);

            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
            };

            Subject.IsUpgradable(
                        profile,
                        new List<QualityModel> { new QualityModel(Quality.MP3_256, new Revision(version: 1)) },
                        new List<CustomFormat>(),
                        new QualityModel(Quality.MP3_256, new Revision(version: 2)),
                        new List<CustomFormat>())
                    .Should().BeFalse();
        }
    }
}
