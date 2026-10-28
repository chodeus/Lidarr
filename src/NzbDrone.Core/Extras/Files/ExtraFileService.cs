using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Extras.Files
{
    public interface IExtraFileService<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        List<TExtraFile> GetFilesByArtist(int artistId);
        List<TExtraFile> GetFilesByTrackFile(int trackFileId);
        TExtraFile FindByPath(int artistId, string path);
        void Upsert(TExtraFile extraFile);
        void Upsert(List<TExtraFile> extraFiles);
        void Delete(int id);
        void DeleteMany(IEnumerable<int> ids);
    }

    public interface IRelinkExtraFiles
    {
        void Relink(int fromTrackFileId, int toTrackFileId);
    }

    public abstract class ExtraFileService<TExtraFile> : IExtraFileService<TExtraFile>,
                                                         IRelinkExtraFiles,
                                                         IHandleAsync<ArtistsDeletedEvent>,
                                                         IHandleAsync<AlbumDeletedEvent>,
                                                         IHandle<TrackFileDeletedEvent>
        where TExtraFile : ExtraFile, new()
    {
        private readonly IExtraFileRepository<TExtraFile> _repository;
        private readonly IArtistService _artistService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly Logger _logger;

        public ExtraFileService(IExtraFileRepository<TExtraFile> repository,
                                IArtistService artistService,
                                IDiskProvider diskProvider,
                                IRecycleBinProvider recycleBinProvider,
                                Logger logger)
        {
            _repository = repository;
            _artistService = artistService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _logger = logger;
        }

        public List<TExtraFile> GetFilesByArtist(int artistId)
        {
            return _repository.GetFilesByArtist(artistId);
        }

        public List<TExtraFile> GetFilesByTrackFile(int trackFileId)
        {
            return _repository.GetFilesByTrackFile(trackFileId);
        }

        public TExtraFile FindByPath(int artistId, string path)
        {
            return _repository.FindByPath(artistId, path);
        }

        public void Upsert(TExtraFile extraFile)
        {
            Upsert(new List<TExtraFile> { extraFile });
        }

        public void Upsert(List<TExtraFile> extraFiles)
        {
            extraFiles.ForEach(m =>
            {
                m.LastUpdated = DateTime.UtcNow;

                if (m.Id == 0)
                {
                    m.Added = m.LastUpdated;
                }
            });

            _repository.InsertMany(extraFiles.Where(m => m.Id == 0).ToList());
            _repository.UpdateMany(extraFiles.Where(m => m.Id > 0).ToList());
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            _repository.DeleteMany(ids);
        }

        public void HandleAsync(ArtistsDeletedEvent message)
        {
            _repository.DeleteForArtists(message.Artists.Select(x => x.Id).ToList());
        }

        // Its track files go to the recycle bin with no events of their own, so their extras go here
        public void HandleAsync(AlbumDeletedEvent message)
        {
            if (!message.DeleteFiles)
            {
                return;
            }

            var artist = message.Album.Artist.Value;

            Recycle(artist, _repository.GetFilesByAlbum(artist.Id, message.Album.Id));
            _repository.DeleteForAlbum(artist.Id, message.Album.Id);
        }

        public void Relink(int fromTrackFileId, int toTrackFileId)
        {
            var extraFiles = _repository.GetFilesByTrackFile(fromTrackFileId);

            extraFiles.ForEach(e => e.TrackFileId = toTrackFileId);
            _repository.SetFields(extraFiles, e => e.TrackFileId);
        }

        public void Handle(TrackFileDeletedEvent message)
        {
            var trackFile = message.TrackFile;

            // The same file imported again under a new row: ImportApprovedTracks relinks its extras to that row
            if (message.Reason == DeleteMediaFileReason.ManualOverride)
            {
                return;
            }

            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing track file from DB as part of cleanup routine, not deleting extra files from disk.");
            }
            else
            {
                Recycle(trackFile.Artist.Value, _repository.GetFilesByTrackFile(trackFile.Id));
            }

            _logger.Debug("Deleting Extra from database for track file: {0}", trackFile);
            _repository.DeleteForTrackFile(trackFile.Id);
        }

        protected void Recycle(Artist artist, IEnumerable<TExtraFile> extraFiles)
        {
            foreach (var extra in extraFiles)
            {
                var path = Path.Combine(artist.Path, extra.RelativePath);

                if (_diskProvider.FileExists(path))
                {
                    // Send to the recycling bin so they can be recovered if necessary
                    var subfolder = _diskProvider.GetParentFolder(artist.Path).GetRelativePath(_diskProvider.GetParentFolder(path));
                    _recycleBinProvider.DeleteFile(path, subfolder);
                }
            }
        }
    }
}
