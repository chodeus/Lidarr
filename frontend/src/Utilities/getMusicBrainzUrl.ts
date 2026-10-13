const MUSICBRAINZ_ID =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Other metadata sources' IDs have no MusicBrainz page
export default function getMusicBrainzUrl(
  entity: 'artist' | 'release-group',
  foreignId: string
) {
  return MUSICBRAINZ_ID.test(foreignId)
    ? `https://musicbrainz.org/${entity}/${foreignId}`
    : null;
}
