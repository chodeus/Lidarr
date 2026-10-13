namespace NzbDrone.Core.MetadataSource.Sources
{
    // Sources in one family share IDs, so they can stand in for each other; plugins name their own families
    public static class MetadataIdFamilies
    {
        public const string MusicBrainz = "MusicBrainz";
        public const string Deezer = "Deezer";
    }
}
