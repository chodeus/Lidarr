using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class extra_files_nullable_track_fileFixture : MigrationTest<extra_files_nullable_track_file>
    {
        [Test]
        public void should_keep_existing_rows_and_store_one_without_a_track_file()
        {
            var db = WithMigrationTestDb(c => c.Insert.IntoTable("ExtraFiles").Row(new
            {
                ArtistId = 1,
                AlbumId = 2,
                TrackFileId = 3,
                RelativePath = "Album/01 - Song.log",
                Extension = ".log",
                Added = "2026-01-01 00:00:00Z",
                LastUpdated = "2026-01-01 00:00:00Z"
            }));

            db.Query("INSERT INTO \"ExtraFiles\" (\"ArtistId\", \"AlbumId\", \"TrackFileId\", \"RelativePath\", \"Extension\", \"Added\", \"LastUpdated\") VALUES (1, 2, NULL, 'Album/Album.cue', '.cue', '2026-01-01 00:00:00Z', '2026-01-01 00:00:00Z')");

            var rows = db.Query<ExtraFile915>("SELECT \"RelativePath\", \"TrackFileId\" FROM \"ExtraFiles\" ORDER BY \"Id\"");

            rows.Select(r => (r.RelativePath, r.TrackFileId)).Should().Equal(("Album/01 - Song.log", (int?)3), ("Album/Album.cue", (int?)null));
        }
    }

    internal class ExtraFile915
    {
        public string RelativePath { get; set; }
        public int? TrackFileId { get; set; }
    }
}
