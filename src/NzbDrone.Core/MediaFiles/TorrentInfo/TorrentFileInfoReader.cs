using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonoTorrent;
using NLog;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    public interface ITorrentFileInfoReader
    {
        string GetHashFromTorrentFile(byte[] fileContents);

        // Default keeps implementations compiled against develop loading; when it throws, the grab-time file check is skipped and the grab goes ahead
        List<string> GetFileNamesFromTorrentFile(byte[] fileContents) => throw new NotSupportedException();
    }

    public class TorrentFileInfoReader : ITorrentFileInfoReader
    {
        private readonly Logger _logger;

        public TorrentFileInfoReader(Logger logger)
        {
            _logger = logger;
        }

        public string GetHashFromTorrentFile(byte[] fileContents)
        {
            try
            {
                return Torrent.Load(fileContents).InfoHashes.V1OrV2.ToHex();
            }
            catch
            {
                _logger.Trace("Invalid torrent file contents: {0}", Encoding.ASCII.GetString(fileContents));
                throw;
            }
        }

        public List<string> GetFileNamesFromTorrentFile(byte[] fileContents)
        {
            try
            {
                return Torrent.Load(fileContents).Files.Select(f => f.Path).ToList();
            }
            catch
            {
                _logger.Trace("Invalid torrent file contents: {0}", Encoding.ASCII.GetString(fileContents));
                throw;
            }
        }
    }
}
