using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Extras.Metadata.Files
{
    public interface IMetadataFileService : IExtraFileService<MetadataFile>
    {
    }

    public class MetadataFileService : ExtraFileService<MetadataFile>, IMetadataFileService
    {
        // Develop's constructor, kept for plugins built against it
        public MetadataFileService(IExtraFileRepository<MetadataFile> repository, IArtistService artistService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : this(repository, artistService, null, diskProvider, recycleBinProvider, logger)
        {
        }

        public MetadataFileService(IExtraFileRepository<MetadataFile> repository, IArtistService artistService, IMediaFileService mediaFileService, IDiskProvider diskProvider, IRecycleBinProvider recycleBinProvider, Logger logger)
            : base(repository, artistService, mediaFileService, diskProvider, recycleBinProvider, logger)
        {
        }
    }
}
