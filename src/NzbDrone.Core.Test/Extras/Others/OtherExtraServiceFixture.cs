using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Others
{
    // The real repository over the test database
    [TestFixture]
    public class OtherExtraServiceFixture : DbTest<OtherExtraService, OtherExtraFile>
    {
        private Artist _artist;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IDiskProvider>(new Mock<DiskProviderBase>(new FileSystem()) { CallBase = true }.Object);
            Mocker.SetConstant<IExtraFileRepository<OtherExtraFile>>(Mocker.Resolve<OtherExtraFileRepository>());
            Mocker.SetConstant<IOtherExtraFileService>(Mocker.Resolve<OtherExtraFileService>());

            _artist = Builder<Artist>.CreateNew().With(a => a.Id = 1).With(a => a.Path = Path.Combine(TempFolder, "Music", "Artist")).Build();
        }

        private void GivenReleaseFile(int albumId, string relativePath)
        {
            var path = Path.Combine(_artist.Path, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "rip log");
            Db.Insert(new OtherExtraFile { ArtistId = _artist.Id, AlbumId = albumId, TrackFileId = null, RelativePath = relativePath, Extension = ".log", Added = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
        }

        [Test]
        public void should_move_a_release_file_to_the_folder_its_albums_tracks_moved_to_under_its_own_name()
        {
            GivenReleaseFile(7, Path.Combine("Old", "Album.log"));
            GivenReleaseFile(8, Path.Combine("Other", "Other.log"));
            var trackFiles = new List<TrackFile>
            {
                new TrackFile { Id = 1, AlbumId = 7, Path = Path.Combine(_artist.Path, "New", "CD1", "01 - Song.flac") },
                new TrackFile { Id = 2, AlbumId = 7, Path = Path.Combine(_artist.Path, "New", "CD2", "01 - Song.flac") }
            };

            trackFiles.ForEach(f => Directory.CreateDirectory(Path.GetDirectoryName(f.Path)));

            Subject.MoveFilesAfterRename(_artist, trackFiles);

            AllStoredModels.Select(f => f.RelativePath).Should().BeEquivalentTo(Path.Combine("New", "Album.log"), Path.Combine("Other", "Other.log"));
            File.Exists(Path.Combine(_artist.Path, "New", "Album.log")).Should().BeTrue();
            File.Exists(Path.Combine(_artist.Path, "Old", "Album.log")).Should().BeFalse();
        }
    }
}
