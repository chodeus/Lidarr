using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // Not a not-found: no source can answer right now, so refresh keeps what it has
    public class MetadataSourceUnavailableException : NzbDroneClientException
    {
        public MetadataSourceUnavailableException(string message, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, args)
        {
        }
    }
}
