import { PropertyFilter } from 'App/State/AppState';
import { filterTypes } from 'Helpers/Props';

export interface StatisticsParams {
  rootFolderPaths?: string[];
  rootFolderPathsNot?: boolean;
  tagIds?: number[];
  tagIdsNot?: boolean;
  qualityProfileIds?: number[];
  qualityProfileIdsNot?: boolean;
  metadataProfileIds?: number[];
  metadataProfileIdsNot?: boolean;
  monitored?: boolean;
}

type ListParam =
  | 'rootFolderPaths'
  | 'tagIds'
  | 'qualityProfileIds'
  | 'metadataProfileIds';

// Keys match filterBuilderProps in Store/Actions/statisticsActions
const LIST_PARAMS: Record<string, ListParam> = {
  rootFolderPath: 'rootFolderPaths',
  tags: 'tagIds',
  qualityProfileId: 'qualityProfileIds',
  metadataProfileId: 'metadataProfileIds',
};

// Conditions the API can't express are counted, so the page can warn that they were ignored
function getStatisticsParams(propertyFilters: PropertyFilter[]) {
  const params: StatisticsParams = {};
  const usedKeys = new Set<string>();
  let ignoredCount = 0;

  propertyFilters.forEach(({ key, value, type }) => {
    const values = (Array.isArray(value) ? value : [value]).filter(
      (v) => v != null
    );

    if (!values.length) {
      return;
    }

    if (usedKeys.has(key)) {
      ignoredCount++;
      return;
    }

    usedKeys.add(key);

    const isNegated =
      type === filterTypes.NOT_EQUAL || type === filterTypes.NOT_CONTAINS;

    if (key === 'monitored') {
      const matchesBoth = new Set(values.map(Boolean)).size > 1;

      if (!matchesBoth) {
        params.monitored = isNegated ? !values[0] : Boolean(values[0]);
      } else if (isNegated) {
        // "Is not true, false" matches no artist, which the API can't express
        ignoredCount++;
      }

      return;
    }

    const listParam = LIST_PARAMS[key];

    if (!listParam) {
      ignoredCount++;
      return;
    }

    if (listParam === 'rootFolderPaths') {
      params.rootFolderPaths = values.map(String);
    } else {
      // With no tags yet the builder takes free text, which is no id: the API rejects NaN
      const ids = values.map(Number).filter(Number.isFinite);

      if (ids.length < values.length) {
        ignoredCount++;
      }

      if (!ids.length) {
        return;
      }

      params[listParam] = ids;
    }

    // A missing flag must stay absent: jQuery sends undefined as an empty value, which a bool won't bind
    if (isNegated) {
      params[`${listParam}Not`] = true;
    }
  });

  return { params, ignoredCount };
}

export default getStatisticsParams;
