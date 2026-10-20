using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MediaFiles.TrackImport
{
    public class ReplacedAlbum
    {
        public Artist Artist { get; set; }
        public Album Album { get; set; }
        public string RootFolder { get; set; }
        public DateTime Started { get; set; }
        public List<int> ReleaseIds { get; set; } = new List<int>();
        public List<ReplacedFile> Files { get; } = new List<ReplacedFile>();
        public List<ReplacedExtra> Extras { get; } = new List<ReplacedExtra>();

        public List<string> BackupFolders(IDiskProvider diskProvider)
        {
            return Files.Select(f => f.BackupPath).Concat(Extras.Select(e => e.BackupPath)).Where(p => p != null).Select(diskProvider.GetParentFolder).Distinct().ToList();
        }
    }

    public class ReplacedFile
    {
        public TrackFile TrackFile { get; set; }
        public string BackupPath { get; set; }
        public List<int> TrackIds { get; set; }
        public bool Recycled { get; set; }
        public bool Restored { get; set; }
    }

    public class ReplacedExtra
    {
        public ExtraFile Row { get; set; }
        public int TrackFileId { get; set; }
        public string OriginalPath { get; set; }
        public string BackupPath { get; set; }
        public Action<ExtraFile> Upsert { get; set; }
    }
}
