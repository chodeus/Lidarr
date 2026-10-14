import ModelBase from 'App/ModelBase';

export type MetadataSourceRole = 'primary' | 'fallback' | 'supplementary';

export type MetadataSourceCombination = 'or' | 'and';

export interface MetadataSourceProfileItem {
  metadataSourceId: number;
  role: MetadataSourceRole;
}

interface MetadataSourceProfile extends ModelBase {
  name: string;
  tags: number[];
  order: number;
  primaryCombination: MetadataSourceCombination;
  items: MetadataSourceProfileItem[];
}

export default MetadataSourceProfile;
