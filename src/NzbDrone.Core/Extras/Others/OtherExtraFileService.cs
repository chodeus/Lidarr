using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Extras.Others
{
    public interface IOtherExtraFileService : IExtraFileService<OtherExtraFile>
    {
        // A default body, so an implementation built against develop still loads
        void DeleteReleaseFiles(Artist artist, int albumId)
        {
        }
    }

    public class OtherExtraFileService : ExtraFileService<OtherExtraFile>, IOtherExtraFileService
    {
        private readonly IExtraFileRepository<OtherExtraFile> _repository;

        public OtherExtraFileService(IExtraFileRepository<OtherExtraFile> repository, IArtistService artistService, IMediaFileService mediaFileService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : base(repository, artistService, mediaFileService, diskProvider, recycleBinProvider, logger)
        {
            _repository = repository;
        }

        // A release's own files, such as its .cue or rip log, are stored with the album and no track file
        public void DeleteReleaseFiles(Artist artist, int albumId)
        {
            var releaseFiles = _repository.GetFilesByAlbum(artist.Id, albumId).Where(f => f.TrackFileId == null).ToList();

            Recycle(artist, releaseFiles);
            DeleteMany(releaseFiles.Select(f => f.Id));
        }
    }
}
