import ModelBase from 'App/ModelBase';
import { Field } from './ImportList';

interface MetadataSource extends ModelBase {
  enable: boolean;
  name: string;
  fields: Field[];
  implementationName: string;
  implementation: string;
  configContract: string;
  infoLink: string;
}

export default MetadataSource;
