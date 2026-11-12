using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Extras
{
    public interface IExtraService
    {
        void ImportTrack(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly);

        // Run before the track file is saved, as develop runs ImportTrack: its rows come back without the track file's id
        List<ExtraFile> ImportTrackExtras(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            ImportTrack(localTrack, trackFile, isReadOnly);
            return new List<ExtraFile>();
        }

        void LinkTrackExtras(IEnumerable<ExtraFile> extraFiles, TrackFile trackFile)
        {
        }

        // A default body, so an implementation built against develop still loads
        void RelinkTrackFile(int previousTrackFileId, TrackFile trackFile)
        {
        }
    }

    public class ExtraService : IExtraService,
                                IHandle<MediaCoversUpdatedEvent>,
                                IHandle<TrackFolderCreatedEvent>,
                                IHandle<ArtistScannedEvent>,
                                IHandle<ArtistRenamedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IAlbumService _albumService;
        private readonly ITrackService _trackService;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly List<IManageExtraFiles> _extraFileManagers;
        private readonly List<IRelinkExtraFiles> _extraFileRelinkers;
        private readonly Logger _logger;

        // Develop's constructor, kept for plugins built against it; a file imported again then loses its extras' links, as in develop
        public ExtraService(IMediaFileService mediaFileService,
                            IAlbumService albumService,
                            ITrackService trackService,
                            IDiskProvider diskProvider,
                            IConfigService configService,
                            IEnumerable<IManageExtraFiles> extraFileManagers,
                            Logger logger)
            : this(mediaFileService, albumService, trackService, diskProvider, configService, extraFileManagers, Enumerable.Empty<IRelinkExtraFiles>(), logger)
        {
        }

        public ExtraService(IMediaFileService mediaFileService,
                            IAlbumService albumService,
                            ITrackService trackService,
                            IDiskProvider diskProvider,
                            IConfigService configService,
                            IEnumerable<IManageExtraFiles> extraFileManagers,
                            IEnumerable<IRelinkExtraFiles> extraFileRelinkers,
                            Logger logger)
        {
            _mediaFileService = mediaFileService;
            _albumService = albumService;
            _trackService = trackService;
            _diskProvider = diskProvider;
            _configService = configService;
            _extraFileManagers = extraFileManagers.OrderBy(e => e.Order).ToList();
            _extraFileRelinkers = extraFileRelinkers.ToList();
            _logger = logger;
        }

        // Each kind on its own, so one that fails leaves the others linked
        public void RelinkTrackFile(int previousTrackFileId, TrackFile trackFile)
        {
            foreach (var relinker in _extraFileRelinkers)
            {
                try
                {
                    relinker.Relink(previousTrackFileId, trackFile);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't move the {0} of {1} to its new row", relinker.GetType().Name, trackFile);
                }
            }
        }

        public void ImportTrack(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            ImportTrackExtras(localTrack, trackFile, isReadOnly);
        }

        public List<ExtraFile> ImportTrackExtras(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            var written = new List<ExtraFile>();

            try
            {
                written.AddRange(ImportExtraFilesFor(localTrack, trackFile, isReadOnly));
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't import extra files for {0}", localTrack);
            }

            // Each on its own: one that throws, such as a plugin's converter, keeps the others' rows
            foreach (var extraFileManager in _extraFileManagers)
            {
                try
                {
                    written.AddRange(extraFileManager.CreateAfterTrackImport(localTrack.Artist, trackFile) ?? Enumerable.Empty<ExtraFile>());
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't create the {0} for {1}", extraFileManager.GetType().Name, trackFile);
                }
            }

            return written;
        }

        // Each kind on its own, so one that fails leaves the others linked
        public void LinkTrackExtras(IEnumerable<ExtraFile> extraFiles, TrackFile trackFile)
        {
            var files = extraFiles?.ToList() ?? new List<ExtraFile>();
            if (files.Count == 0)
            {
                return;
            }

            foreach (var relinker in _extraFileRelinkers)
            {
                try
                {
                    relinker.Link(files, trackFile);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't link the {0} written for {1}", relinker.GetType().Name, trackFile);
                }
            }
        }

        public void ImportExtraFiles(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            ImportExtraFilesFor(localTrack, trackFile, isReadOnly);
        }

        private List<ExtraFile> ImportExtraFilesFor(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            var imported = new List<ExtraFile>();

            if (!_configService.ImportExtraFiles)
            {
                return imported;
            }

            var sourcePath = localTrack.Path;
            var sourceFolder = _diskProvider.GetParentFolder(sourcePath);
            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePath);
            var files = _diskProvider.GetFiles(sourceFolder, false);

            var wantedExtensions = _configService.ExtraFileExtensions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                                     .Select(e => e.Trim(' ', '.'))
                                                                     .ToList();

            var matchingFilenames = files.Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(sourceFileName, StringComparison.InvariantCultureIgnoreCase)).ToList();
            var filteredFilenames = new List<string>();
            var hasNfo = false;

            foreach (var matchingFilename in matchingFilenames)
            {
                // Filter out duplicate NFO files
                if (matchingFilename.EndsWith(".nfo", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (hasNfo)
                    {
                        continue;
                    }

                    hasNfo = true;
                }

                filteredFilenames.Add(matchingFilename);
            }

            foreach (var matchingFilename in filteredFilenames)
            {
                var matchingExtension = wantedExtensions.FirstOrDefault(e => matchingFilename.EndsWith(e));

                if (matchingExtension == null)
                {
                    continue;
                }

                try
                {
                    foreach (var extraFileManager in _extraFileManagers)
                    {
                        var extension = Path.GetExtension(matchingFilename);
                        var extraFile = extraFileManager.Import(localTrack.Artist, trackFile, matchingFilename, extension, isReadOnly);

                        if (extraFile != null)
                        {
                            imported.Add(extraFile);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import extra file: {0}", matchingFilename);
                }
            }

            return imported;
        }

        public void Handle(MediaCoversUpdatedEvent message)
        {
            if (message.Updated)
            {
                var artist = message.Artist ?? message.Album.Artist;

                foreach (var extraFileManager in _extraFileManagers)
                {
                    extraFileManager.CreateAfterMediaCoverUpdate(artist);
                }
            }
        }

        public void Handle(ArtistScannedEvent message)
        {
            var artist = message.Artist;

            var trackFiles = GetTrackFiles(artist.Id);

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.CreateAfterArtistScan(artist, trackFiles);
            }
        }

        public void Handle(TrackFolderCreatedEvent message)
        {
            var artist = message.Artist;
            var album = _albumService.GetAlbum(message.TrackFile.AlbumId);

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.CreateAfterTrackFolder(artist, album, message.ArtistFolder, message.AlbumFolder);
            }
        }

        public void Handle(ArtistRenamedEvent message)
        {
            var artist = message.Artist;
            var trackFiles = GetTrackFiles(artist.Id);

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.MoveFilesAfterRename(artist, trackFiles);
            }
        }

        private List<TrackFile> GetTrackFiles(int artistId)
        {
            var trackFiles = _mediaFileService.GetFilesByArtist(artistId);
            var tracks = _trackService.GetTracksByArtist(artistId);

            foreach (var trackFile in trackFiles)
            {
                var localTrackFile = trackFile;
                trackFile.Tracks = tracks.Where(e => e.TrackFileId == localTrackFile.Id).ToList();
            }

            return trackFiles;
        }
    }
}
