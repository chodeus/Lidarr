using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Lyrics;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupOrphanedLyricFilesFixture : DbTest<CleanupOrphanedLyricFiles, LyricFile>
    {
        private TrackFile GivenTrackFile(string path = null)
        {
            var trackFile = Builder<TrackFile>.CreateNew()
                                              .With(h => h.Quality = new QualityModel())
                                              .With(h => h.Path = path ?? "track.flac")
                                              .With(h => h.AlbumId = 7)
                                              .BuildNew();

            Db.Insert(trackFile);
            return trackFile;
        }

        private Artist GivenArtist()
        {
            var artist = Builder<Artist>.CreateNew()
                                        .BuildNew();

            Db.Insert(artist);
            return artist;
        }

        [Test]
        public void should_delete_lyric_files_that_dont_have_a_coresponding_artist()
        {
            var trackFile = GivenTrackFile();

            var lyricFile = Builder<LyricFile>.CreateNew()
                                                  .With(m => m.TrackFileId = trackFile.Id)
                                                  .BuildNew();

            Db.Insert(lyricFile);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_not_delete_lyric_files_that_have_a_coresponding_artist_and_track_file()
        {
            var artist = GivenArtist();
            var trackFile = GivenTrackFile();

            var lyricFile = Builder<LyricFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.TrackFileId = trackFile.Id)
                                                  .BuildNew();

            Db.Insert(lyricFile);
            Subject.Clean();
            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void should_delete_lyric_files_that_dont_have_a_coresponding_track_file()
        {
            var artist = GivenArtist();

            var lyricFile = Builder<LyricFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.TrackFileId = 10)
                                                  .BuildNew();

            Db.Insert(lyricFile);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_link_a_row_stored_without_its_track_file_to_the_track_file_it_was_named_after()
        {
            var artist = Builder<Artist>.CreateNew()
                                        .With(a => a.Path = @"C:\Music\Artist".AsOsAgnostic())
                                        .BuildNew();

            Db.Insert(artist);

            var song = GivenTrackFile(Path.Combine(artist.Path, "Album", "01 - Song.flac"));
            GivenTrackFile(Path.Combine(artist.Path, "Album", "Mr.flac"));
            var blueSky = GivenTrackFile(Path.Combine(artist.Path, "Album", "Mr. Blue Sky.flac"));

            foreach (var name in new[] { "01 - Song.lrc", "Mr. Blue Sky.lrc", "01 - Songs Intro.lrc", "Old Name.lrc" })
            {
                Db.Insert(Builder<LyricFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.AlbumId = 1)
                                                  .With(m => m.TrackFileId = 0)
                                                  .With(m => m.RelativePath = Path.Combine("Album", name))
                                                  .BuildNew());
            }

            Subject.Clean();

            AllStoredModels.Select(m => (Path.GetFileName(m.RelativePath), m.TrackFileId, m.AlbumId)).Should().BeEquivalentTo(new[]
            {
                ("01 - Song.lrc", (int?)song.Id, (int?)song.AlbumId),
                ("Mr. Blue Sky.lrc", (int?)blueSky.Id, (int?)blueSky.AlbumId),
                ("01 - Songs Intro.lrc", (int?)0, (int?)1),
                ("Old Name.lrc", (int?)0, (int?)1)
            });
        }
    }
}
