using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MetadataSource.Sources.Discogs;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Sources.Discogs
{
    [TestFixture]
    public class DiscogsCacheFixture : CoreTest<DiscogsCache>
    {
        private string _appData;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);
            Mocker.GetMock<IAppFolderInfo>().Setup(a => a.AppDataFolder).Returns(_appData);
        }

        private static DiscogsReleaseResource Release()
        {
            return new DiscogsReleaseResource
            {
                Id = 7,
                Title = "Placeholder Album",
                MasterId = 3,
                Artists = new List<DiscogsArtistCreditResource> { new() { Id = 3, Name = "Placeholder Artist" } },
                Tracklist = new List<DiscogsTrackResource> { new() { Position = "A1", Type = "track", Title = "First", Duration = "3:00" } }
            };
        }

        private long Rows()
        {
            using var connection = new SQLiteConnection("Data Source=" + Path.Combine(_appData, DiscogsCache.FileName));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Entries";
            return (long)command.ExecuteScalar();
        }

        [Test]
        public void keeps_a_release_in_its_own_file_until_it_expires()
        {
            Subject.Set("releases/7", Release(), TimeSpan.FromDays(30));

            Subject.TryGet<DiscogsReleaseResource>("releases/7", out var cached).Should().BeTrue();

            cached.Should().BeEquivalentTo(Release());
            File.Exists(Path.Combine(_appData, DiscogsCache.FileName)).Should().BeTrue();
        }

        [Test]
        public void an_expired_entry_is_not_returned()
        {
            Subject.Set("releases/7", Release(), TimeSpan.FromSeconds(-1));

            Subject.TryGet<DiscogsReleaseResource>("releases/7", out _).Should().BeFalse();
        }

        [Test]
        public void housekeeping_removes_only_expired_entries()
        {
            Subject.Set("releases/7", Release(), TimeSpan.FromSeconds(-1));
            Subject.Set("releases/8", Release(), TimeSpan.FromDays(30));

            new CleanupExpiredDiscogsCache(Subject).Clean();

            Rows().Should().Be(1);
            Subject.TryGet<DiscogsReleaseResource>("releases/8", out _).Should().BeTrue();
        }

        [Test]
        public void an_entry_written_for_other_resource_fields_is_not_read()
        {
            Subject.Set("releases/7", Release(), TimeSpan.FromDays(30));

            using (var connection = new SQLiteConnection("Data Source=" + Path.Combine(_appData, DiscogsCache.FileName)))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Entries SET Key = 'releases/7'";
                command.ExecuteNonQuery();
            }

            Subject.TryGet<DiscogsReleaseResource>("releases/7", out _).Should().BeFalse();
        }

        [Test]
        public void an_unusable_cache_is_skipped()
        {
            Mocker.GetMock<IAppFolderInfo>().Setup(a => a.AppDataFolder).Returns(Path.Combine(_appData, "missing", "folder"));

            Subject.Set("releases/7", Release(), TimeSpan.FromDays(30));

            Subject.TryGet<DiscogsReleaseResource>("releases/7", out _).Should().BeFalse();
            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
