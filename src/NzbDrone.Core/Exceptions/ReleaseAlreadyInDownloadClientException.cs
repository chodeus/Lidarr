using System;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Exceptions
{
    // The client holds this exact torrent already, so adding it again can't succeed
    public class ReleaseAlreadyInDownloadClientException : DownloadClientRejectedReleaseException
    {
        public ReleaseAlreadyInDownloadClientException(ReleaseInfo release, string infoHash, int downloadClientId, string downloadClientName, string message, Exception innerException)
            : base(release, message, innerException)
        {
            InfoHash = infoHash;
            DownloadClientId = downloadClientId;
            DownloadClientName = downloadClientName;
        }

        public string InfoHash { get; }

        // The client that refused it, which a caller trying several clients in turn can't otherwise tell
        public int DownloadClientId { get; }
        public string DownloadClientName { get; }
    }
}
