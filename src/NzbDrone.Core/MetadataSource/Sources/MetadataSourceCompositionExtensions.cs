using DryIoc;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public static class MetadataSourceCompositionExtensions
    {
        // Call after every assembly, plugins included, is registered: a plugin's own lookup must not bypass the profiles
        public static IContainer AddMetadataSources(this IContainer container)
        {
            container.RegisterMany(new[]
                {
                    typeof(IProvideArtistInfo),
                    typeof(IProvideAlbumInfo),
                    typeof(ISearchForNewArtist),
                    typeof(ISearchForNewAlbum),
                    typeof(ISearchForNewEntity),
                    typeof(IMetadataSourceLabels)
                },
                typeof(MetadataSourceAggregator),
                Reuse.Singleton,
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            container.Register<IMetadataRequestBuilder, MetadataRequestBuilder>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            return container;
        }
    }
}
