namespace NzbDrone.Core.MetadataSource.Sources.Discogs
{
    public class DiscogsTokenRejectedException : MetadataSourceUnavailableException
    {
        public DiscogsTokenRejectedException()
            : base("Discogs rejected the personal access token")
        {
        }
    }
}
