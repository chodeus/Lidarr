import _ from 'lodash';
import { createAction } from 'redux-actions';
import { update } from 'Store/Actions/baseActions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler from 'Store/Actions/Creators/createSaveProviderHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';

//
// Variables

const section = 'settings.metadataSourceProfiles';

//
// Actions Types

export const FETCH_METADATA_SOURCE_PROFILES = 'settings/metadataSourceProfiles/fetchMetadataSourceProfiles';
export const FETCH_METADATA_SOURCE_PROFILE_SCHEMA = 'settings/metadataSourceProfiles/fetchMetadataSourceProfileSchema';
export const SAVE_METADATA_SOURCE_PROFILE = 'settings/metadataSourceProfiles/saveMetadataSourceProfile';
export const DELETE_METADATA_SOURCE_PROFILE = 'settings/metadataSourceProfiles/deleteMetadataSourceProfile';
export const REORDER_METADATA_SOURCE_PROFILE = 'settings/metadataSourceProfiles/reorderMetadataSourceProfile';
export const SET_METADATA_SOURCE_PROFILE_VALUE = 'settings/metadataSourceProfiles/setMetadataSourceProfileValue';

//
// Action Creators

export const fetchMetadataSourceProfiles = createThunk(FETCH_METADATA_SOURCE_PROFILES);
export const fetchMetadataSourceProfileSchema = createThunk(FETCH_METADATA_SOURCE_PROFILE_SCHEMA);
export const saveMetadataSourceProfile = createThunk(SAVE_METADATA_SOURCE_PROFILE);
export const deleteMetadataSourceProfile = createThunk(DELETE_METADATA_SOURCE_PROFILE);
export const reorderMetadataSourceProfile = createThunk(REORDER_METADATA_SOURCE_PROFILE);

export const setMetadataSourceProfileValue = createAction(SET_METADATA_SOURCE_PROFILE_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: {},
    items: [],
    isSaving: false,
    saveError: null,
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA_SOURCE_PROFILES]: createFetchHandler(section, '/metadatasourceprofile'),
    [FETCH_METADATA_SOURCE_PROFILE_SCHEMA]: createFetchSchemaHandler(section, '/metadatasourceprofile/schema'),
    [SAVE_METADATA_SOURCE_PROFILE]: createSaveProviderHandler(section, '/metadatasourceprofile'),
    [DELETE_METADATA_SOURCE_PROFILE]: (getState, payload, dispatch) => {
      const promise = createRemoveItemHandler(section, '/metadatasourceprofile')(getState, payload, dispatch);

      // Delete renumbers the remaining profiles and reorder finds them by order, so reload them
      promise.done(() => {
        dispatch(fetchMetadataSourceProfiles());
      });

      return promise;
    },

    [REORDER_METADATA_SOURCE_PROFILE]: (getState, payload, dispatch) => {
      const { id, moveIndex } = payload;
      const moveOrder = moveIndex + 1;
      const profiles = getState().settings.metadataSourceProfiles.items;
      const moving = _.find(profiles, { id });

      if (moving.order === moveOrder) {
        return;
      }

      const after = moveIndex > 0 ? _.find(profiles, { order: moveIndex }) : null;
      const afterQueryParam = after ? `afterId=${after.id}` : '';

      const promise = createAjaxRequest({
        method: 'PUT',
        url: `/metadatasourceprofile/reorder/${id}?${afterQueryParam}`
      }).request;

      promise.done((data) => {
        dispatch(update({ section, data }));
      });
    }
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_SOURCE_PROFILE_VALUE]: createSetSettingValueReducer(section)
  }

};
