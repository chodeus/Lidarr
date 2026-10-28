using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupOrphanedExtraFilesFixture : DbTest<CleanupOrphanedExtraFiles, OtherExtraFile>
    {
        private TrackFile GivenTrackFile()
        {
            var trackFile = Builder<TrackFile>.CreateNew()
                                              .With(h => h.Quality = new QualityModel())
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
        public void should_delete_extra_files_that_dont_have_a_coresponding_artist()
        {
            var trackFile = GivenTrackFile();

            var extraFile = Builder<OtherExtraFile>.CreateNew()
                                                  .With(m => m.TrackFileId = trackFile.Id)
                                                  .BuildNew();

            Db.Insert(extraFile);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_not_delete_extra_files_that_have_a_coresponding_artist_and_track_file()
        {
            var artist = GivenArtist();
            var trackFile = GivenTrackFile();

            var extraFile = Builder<OtherExtraFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.TrackFileId = trackFile.Id)
                                                  .BuildNew();

            Db.Insert(extraFile);
            Subject.Clean();
            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void should_delete_extra_files_that_dont_have_a_coresponding_track_file()
        {
            var artist = GivenArtist();

            var extraFile = Builder<OtherExtraFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.TrackFileId = 10)
                                                  .BuildNew();

            Db.Insert(extraFile);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_delete_extra_files_that_have_trackfileid_of_zero()
        {
            var artist = GivenArtist();

            var extraFile = Builder<OtherExtraFile>.CreateNew()
                                                  .With(m => m.ArtistId = artist.Id)
                                                  .With(m => m.TrackFileId = 0)
                                                  .BuildNew();

            Db.Insert(extraFile);
            Subject.Clean();
            AllStoredModels.Should().BeEmpty();
        }
    }
}
