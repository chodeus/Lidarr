using System;

namespace Lidarr.Api.V1.Indexers
{
    public class ReleaseHistoryResource
    {
        public DateTime Grabbed { get; set; }
        public DateTime? Failed { get; set; }
    }
}
