using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
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

        // Default bodies, so an implementation built against develop still loads
        void ImportAlbum(Artist artist, List<string> sourceFolders, List<TrackFile> trackFiles, ISet<string> albumAudio, bool isReadOnly, bool replacePrevious)
        {
        }

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
                    relinker.Relink(previousTrackFileId, trackFile.Id);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't move the {0} of {1} to its new row", relinker.GetType().Name, trackFile);
                }
            }
        }

        public void ImportTrack(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            ImportExtraFiles(localTrack, trackFile, isReadOnly);

            CreateAfterTrackImport(localTrack.Artist, trackFile);
        }

        public void ImportExtraFiles(LocalTrack localTrack, TrackFile trackFile, bool isReadOnly)
        {
            if (!_configService.ImportExtraFiles)
            {
                return;
            }

            var sourcePath = localTrack.Path;
            var sourceFolder = _diskProvider.GetParentFolder(sourcePath);
            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePath);
            var files = _diskProvider.GetFiles(sourceFolder, false);

            var wantedExtensions = WantedExtensions();

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
                if (!IsWanted(matchingFilename, wantedExtensions))
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
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import extra file: {0}", matchingFilename);
                }
            }
        }

        // A release's own files, such as its .cue or rip log, go once into the album's folder; albumAudio holds every file this album's decisions came from
        public void ImportAlbum(Artist artist, List<string> sourceFolders, List<TrackFile> trackFiles, ISet<string> albumAudio, bool isReadOnly, bool replacePrevious)
        {
            var releaseFiles = new List<string>();

            if (_configService.ImportExtraFiles)
            {
                var wantedExtensions = WantedExtensions();

                releaseFiles = sourceFolders.SelectMany(folder => ReleaseFiles(folder, albumAudio, wantedExtensions)).ToList();
            }

            // Even with Import Extra Files off, as a replaced track's own extras go whatever the setting
            _extraFileManagers.OfType<OtherExtraService>().SingleOrDefault()?.ImportReleaseFiles(artist, trackFiles, releaseFiles, isReadOnly, replacePrevious);
        }

        // Only from a folder holding no other album's audio, and every audio file counts, so a track that didn't import keeps its lyrics out
        private IEnumerable<string> ReleaseFiles(string folder, ISet<string> albumAudio, List<string> wantedExtensions)
        {
            var files = _diskProvider.GetFiles(folder, false).ToList();
            var audio = files.Where(f => MediaFileExtensions.Extensions.Contains(Path.GetExtension(f))).ToList();

            if (!audio.All(albumAudio.Contains))
            {
                _logger.Debug("Not importing release files from {0}: it holds audio of other albums", folder);
                return Enumerable.Empty<string>();
            }

            // An image with its cue sheet beside it isn't a track, so its "Album.cue" and "Album.log" are the release's
            var trackNames = audio.Where(f => !files.Contains(Path.ChangeExtension(f, ".cue"), PathEqualityComparer.Instance))
                                  .Select(Path.GetFileNameWithoutExtension)
                                  .ToList();

            return files.Where(f => IsWanted(f, wantedExtensions) &&
                                    !trackNames.Any(n => Path.GetFileNameWithoutExtension(f).StartsWith(n, StringComparison.InvariantCultureIgnoreCase)));
        }

        private List<string> WantedExtensions()
        {
            return _configService.ExtraFileExtensions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(e => e.Trim(' ', '.'))
                                                     .ToList();
        }

        // ".LOG" matches "log", and "log" doesn't match "backlog"
        private static bool IsWanted(string path, List<string> wantedExtensions)
        {
            return wantedExtensions.Any(e => path.EndsWith("." + e, StringComparison.OrdinalIgnoreCase));
        }

        private void CreateAfterTrackImport(Artist artist, TrackFile trackFile)
        {
            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.CreateAfterTrackImport(artist, trackFile);
            }
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
