using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Extras.Others
{
    public interface IOtherExtraFileService : IExtraFileService<OtherExtraFile>
    {
    }

    public class OtherExtraFileService : ExtraFileService<OtherExtraFile>, IOtherExtraFileService
    {
        // Develop's constructor, kept for plugins built against it
        public OtherExtraFileService(IExtraFileRepository<OtherExtraFile> repository, IArtistService artistService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : this(repository, artistService, null, diskProvider, recycleBinProvider, logger)
        {
        }

        public OtherExtraFileService(IExtraFileRepository<OtherExtraFile> repository, IArtistService artistService, IMediaFileService mediaFileService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : base(repository, artistService, mediaFileService, diskProvider, recycleBinProvider, logger)
        {
        }
    }
}
