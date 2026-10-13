import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createRemoveItemHandler from 'Store/Actions/Creators/createRemoveItemHandler';
import createSaveProviderHandler, { createCancelSaveProviderHandler } from 'Store/Actions/Creators/createSaveProviderHandler';
import createTestProviderHandler, { createCancelTestProviderHandler } from 'Store/Actions/Creators/createTestProviderHandler';
import createSetProviderFieldValueReducer from 'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import selectProviderSchema from 'Utilities/State/selectProviderSchema';

//
// Variables

const section = 'settings.metadataSources';

//
// Actions Types

export const FETCH_METADATA_SOURCES = 'settings/metadataSources/fetchMetadataSources';
export const FETCH_METADATA_SOURCE_SCHEMA = 'settings/metadataSources/fetchMetadataSourceSchema';
export const SELECT_METADATA_SOURCE_SCHEMA = 'settings/metadataSources/selectMetadataSourceSchema';
export const SET_METADATA_SOURCE_VALUE = 'settings/metadataSources/setMetadataSourceValue';
export const SET_METADATA_SOURCE_FIELD_VALUE = 'settings/metadataSources/setMetadataSourceFieldValue';
export const SAVE_METADATA_SOURCE = 'settings/metadataSources/saveMetadataSource';
export const CANCEL_SAVE_METADATA_SOURCE = 'settings/metadataSources/cancelSaveMetadataSource';
export const DELETE_METADATA_SOURCE = 'settings/metadataSources/deleteMetadataSource';
export const TEST_METADATA_SOURCE = 'settings/metadataSources/testMetadataSource';
export const CANCEL_TEST_METADATA_SOURCE = 'settings/metadataSources/cancelTestMetadataSource';

//
// Action Creators

export const fetchMetadataSources = createThunk(FETCH_METADATA_SOURCES);
export const fetchMetadataSourceSchema = createThunk(FETCH_METADATA_SOURCE_SCHEMA);
export const selectMetadataSourceSchema = createAction(SELECT_METADATA_SOURCE_SCHEMA);

export const saveMetadataSource = createThunk(SAVE_METADATA_SOURCE);
export const cancelSaveMetadataSource = createThunk(CANCEL_SAVE_METADATA_SOURCE);
export const deleteMetadataSource = createThunk(DELETE_METADATA_SOURCE);
export const testMetadataSource = createThunk(TEST_METADATA_SOURCE);
export const cancelTestMetadataSource = createThunk(CANCEL_TEST_METADATA_SOURCE);

export const setMetadataSourceValue = createAction(SET_METADATA_SOURCE_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

export const setMetadataSourceFieldValue = createAction(SET_METADATA_SOURCE_FIELD_VALUE, (payload) => {
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
    schema: [],
    selectedSchema: {},
    isSaving: false,
    saveError: null,
    isDeleting: false,
    deleteError: null,
    isTesting: false,
    items: [],
    pendingChanges: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA_SOURCES]: createFetchHandler(section, '/metadatasource'),
    [FETCH_METADATA_SOURCE_SCHEMA]: createFetchSchemaHandler(section, '/metadatasource/schema'),

    [SAVE_METADATA_SOURCE]: createSaveProviderHandler(section, '/metadatasource'),
    [CANCEL_SAVE_METADATA_SOURCE]: createCancelSaveProviderHandler(section),
    [DELETE_METADATA_SOURCE]: createRemoveItemHandler(section, '/metadatasource'),
    [TEST_METADATA_SOURCE]: createTestProviderHandler(section, '/metadatasource'),
    [CANCEL_TEST_METADATA_SOURCE]: createCancelTestProviderHandler(section)
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_SOURCE_VALUE]: createSetSettingValueReducer(section),
    [SET_METADATA_SOURCE_FIELD_VALUE]: createSetProviderFieldValueReducer(section),

    [SELECT_METADATA_SOURCE_SCHEMA]: (state, { payload }) => {
      return selectProviderSchema(state, section, payload, (selectedSchema) => {
        selectedSchema.name = payload.presetName ?? payload.implementationName;
        selectedSchema.implementationName = payload.implementationName;
        selectedSchema.enable = true;

        return selectedSchema;
      });
    }
  }

};
