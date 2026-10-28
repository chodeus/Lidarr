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
        void ImportAlbum(List<LocalTrack> localTracks, List<TrackFile> trackFiles, bool isReadOnly, bool replacePrevious);
        void RelinkTrackFile(int previousTrackFileId, TrackFile trackFile);
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

        public void RelinkTrackFile(int previousTrackFileId, TrackFile trackFile)
        {
            _extraFileRelinkers.ForEach(r => r.Relink(previousTrackFileId, trackFile.Id));
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

        // Files whose names start with no track's, such as a release's .cue or rip log, go once into the album's folder
        public void ImportAlbum(List<LocalTrack> localTracks, List<TrackFile> trackFiles, bool isReadOnly, bool replacePrevious)
        {
            if (!_configService.ImportExtraFiles)
            {
                return;
            }

            var wantedExtensions = WantedExtensions();
            var releaseFiles = localTracks.Select(t => _diskProvider.GetParentFolder(t.Path))
                                          .Distinct(PathEqualityComparer.Instance)
                                          .SelectMany(folder => ReleaseFiles(folder, wantedExtensions))
                                          .ToList();

            _extraFileManagers.OfType<OtherExtraService>().SingleOrDefault()?.ImportReleaseFiles(localTracks.First().Artist, trackFiles, releaseFiles, isReadOnly, replacePrevious);
        }

        // Every audio file counts, so a file of a track that didn't import isn't taken for the release's
        private IEnumerable<string> ReleaseFiles(string folder, List<string> wantedExtensions)
        {
            var files = _diskProvider.GetFiles(folder, false).ToList();
            var trackNames = files.Where(f => MediaFileExtensions.Extensions.Contains(Path.GetExtension(f)))
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
