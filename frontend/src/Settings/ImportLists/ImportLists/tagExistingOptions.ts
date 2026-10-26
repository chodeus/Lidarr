import { TagExisting } from 'typings/ImportList';
import translate from 'Utilities/String/translate';

interface TagExistingOption {
  key: TagExisting;
  value: string;
  hint: string;
}

const tagExistingOptions: TagExistingOption[] = [
  {
    key: 'none',
    get value() {
      return translate('TagExistingNone');
    },
    get hint() {
      return translate('TagExistingNoneHint');
    },
  },
  {
    key: 'add',
    get value() {
      return translate('TagExistingAdd');
    },
    get hint() {
      return translate('TagExistingAddHint');
    },
  },
  {
    key: 'sync',
    get value() {
      return translate('TagExistingSync');
    },
    get hint() {
      return translate('TagExistingSyncHint');
    },
  },
];

export default tagExistingOptions;
