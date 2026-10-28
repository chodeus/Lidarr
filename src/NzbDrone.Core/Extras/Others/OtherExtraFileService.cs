using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Extras.Others
{
    public interface IOtherExtraFileService : IExtraFileService<OtherExtraFile>
    {
        void DeleteReleaseFiles(Artist artist, int albumId);
    }

    public class OtherExtraFileService : ExtraFileService<OtherExtraFile>, IOtherExtraFileService
    {
        private readonly IExtraFileRepository<OtherExtraFile> _repository;
        private readonly IMediaFileService _mediaFileService;

        public OtherExtraFileService(IExtraFileRepository<OtherExtraFile> repository, IArtistService artistService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, IMediaFileService mediaFileService, Logger logger)
            : base(repository, artistService, diskProvider, recycleBinProvider, logger)
        {
            _repository = repository;
            _mediaFileService = mediaFileService;
        }

        // A release's own files, such as its .cue or rip log, are stored with the album and no track file
        public void DeleteReleaseFiles(Artist artist, int albumId)
        {
            var releaseFiles = _repository.GetFilesByAlbum(artist.Id, albumId).Where(f => f.TrackFileId == null).ToList();

            Recycle(artist, releaseFiles);
            DeleteMany(releaseFiles.Select(f => f.Id));
        }

        public override void Handle(TrackFileDeletedEvent message)
        {
            base.Handle(message);

            // Only once a user or a scan removes the album's last file: an import deletes and adds files, and replaces release files itself
            if (message.Reason is not (DeleteMediaFileReason.Manual or DeleteMediaFileReason.MissingFromDisk) ||
                _mediaFileService.GetFilesByAlbum(message.TrackFile.AlbumId).Any())
            {
                return;
            }

            DeleteReleaseFiles(message.TrackFile.Artist.Value, message.TrackFile.AlbumId);
        }
    }
}
