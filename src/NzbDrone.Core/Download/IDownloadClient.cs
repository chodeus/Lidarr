using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public interface IDownloadClient : IProvider
    {
        string Protocol { get; }
        Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer);
        IEnumerable<DownloadClientItem> GetItems();
        DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt);
        void RemoveItem(DownloadClientItem item, bool deleteData);
        DownloadClientInfo GetStatus();
        void MarkItemAsImported(DownloadClientItem downloadClientItem);

        // True only when an add that fails can't have left the release with the client, so the next client may take it
        bool CanFallBackFrom => false;
    }
}
