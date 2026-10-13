using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Core.Music;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource.Sources
{
    public abstract class MetadataSourceBase<TSettings> : IMetadataSource
        where TSettings : IProviderConfig, new()
    {
        public abstract string Name { get; }

        public abstract string IdFamily { get; }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public IEnumerable<ProviderDefinition> DefaultDefinitions => new List<ProviderDefinition>();

        public ProviderDefinition Definition { get; set; }

        protected TSettings Settings => (TSettings)Definition.Settings;

        public virtual ValidationResult Test()
        {
            return new ValidationResult();
        }

        public virtual object RequestAction(string action, IDictionary<string, string> query)
        {
            return null;
        }

        public abstract bool CanHandleId(string foreignId);

        public abstract Artist GetArtistInfo(string foreignArtistId, int metadataProfileId);

        public virtual HashSet<string> GetChangedArtists(DateTime startTime)
        {
            return null;
        }

        public abstract Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string foreignAlbumId);

        public virtual HashSet<string> GetChangedAlbums(DateTime startTime)
        {
            return null;
        }

        public abstract List<Artist> SearchForNewArtist(string title);

        public abstract List<Album> SearchForNewAlbum(string title, string artist);

        public virtual List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds)
        {
            return new List<Album>();
        }

        public abstract List<object> SearchForNewEntity(string title);

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
