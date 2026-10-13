using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource.Sources
{
    // Not a not-found: no source in the profile can answer, so refresh keeps what it has
    public class MetadataSourceUnavailableException : NzbDroneException
    {
        public MetadataSourceUnavailableException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
