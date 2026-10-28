using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Extras.Others
{
    public class OtherExtraService : ExtraFileManager<OtherExtraFile>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly Logger _logger;

        public OtherExtraService(IConfigService configService,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IOtherExtraFileService otherExtraFileService,
                                 IMediaFileAttributeService mediaFileAttributeService,
                                 Logger logger)
            : base(configService, diskProvider, diskTransferService, logger)
        {
            _diskProvider = diskProvider;
            _otherExtraFileService = otherExtraFileService;
            _mediaFileAttributeService = mediaFileAttributeService;
            _logger = logger;
        }

        public override int Order => 2;

        public override IEnumerable<ExtraFile> CreateAfterMediaCoverUpdate(Artist artist)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterArtistScan(Artist artist, List<TrackFile> trackFiles)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterTrackImport(Artist artist, TrackFile trackFile)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterTrackFolder(Artist artist, Album album, string artistFolder, string albumFolder)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> MoveFilesAfterRename(Artist artist, List<TrackFile> trackFiles)
        {
            var extraFiles = _otherExtraFileService.GetFilesByArtist(artist.Id);
            var movedFiles = new List<OtherExtraFile>();

            foreach (var trackFile in trackFiles)
            {
                var extraFilesForTrackFile = extraFiles.Where(m => m.TrackFileId == trackFile.Id).ToList();

                foreach (var extraFile in extraFilesForTrackFile)
                {
                    movedFiles.AddIfNotNull(MoveFile(artist, trackFile, extraFile));
                }
            }

            // A release file follows its album's tracks and keeps its name
            foreach (var album in trackFiles.GroupBy(f => f.AlbumId))
            {
                var folder = album.Select(f => f.Path).ToList().GetLongestCommonPath();

                foreach (var extraFile in extraFiles.Where(m => m.TrackFileId == null && m.AlbumId == album.Key))
                {
                    movedFiles.AddIfNotNull(MoveReleaseFile(artist, extraFile, folder));
                }
            }

            _otherExtraFileService.Upsert(movedFiles);

            return movedFiles;
        }

        public override ExtraFile Import(Artist artist, TrackFile trackFile, string path, string extension, bool readOnly)
        {
            var extraFile = ImportFile(artist, trackFile, path, readOnly, extension, null);

            _mediaFileAttributeService.SetFilePermissions(path);
            _otherExtraFileService.Upsert(extraFile);

            return extraFile;
        }

        // Into the folder of the album's tracks; a replacing import first recycles the previous release's files
        public List<OtherExtraFile> ImportReleaseFiles(Artist artist, List<TrackFile> trackFiles, List<string> paths, bool readOnly, bool replacePrevious)
        {
            var albumId = trackFiles.First().AlbumId;
            var folder = trackFiles.Select(f => f.Path).ToList().GetLongestCommonPath();

            if (replacePrevious)
            {
                _otherExtraFileService.DeleteReleaseFiles(artist, albumId);
            }

            var imported = new List<OtherExtraFile>();

            foreach (var path in paths)
            {
                var destination = Path.Combine(folder, Path.GetFileName(path));

                // Never over a file: one kept from an import that didn't replace, one nothing recorded, or another download folder's of the same name
                if (_diskProvider.FileExists(destination))
                {
                    _logger.Warn("Not importing release file {0}: {1} already exists", path, destination);
                    continue;
                }

                try
                {
                    var releaseFile = ImportReleaseFile(artist, albumId, path, folder, readOnly);
                    var stale = _otherExtraFileService.FindByPath(artist.Id, releaseFile.RelativePath);

                    if (stale != null)
                    {
                        releaseFile.Id = stale.Id;
                        releaseFile.Added = stale.Added;
                    }

                    _mediaFileAttributeService.SetFilePermissions(destination);
                    imported.Add(releaseFile);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import release file: {0}", path);
                }
            }

            _otherExtraFileService.Upsert(imported);

            return imported;
        }
    }
}
