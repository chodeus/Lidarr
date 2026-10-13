import React, { useCallback, useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import {
  saveMetadataSource,
  setMetadataSourceFieldValue,
  setMetadataSourceValue,
  testMetadataSource,
  toggleAdvancedSettings,
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { Field } from 'typings/ImportList';
import MetadataSource from 'typings/MetadataSource';
import { Pending, PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import styles from './EditMetadataSourceModalContent.css';

// Mirrors selectSettings: implementationName and fields are passed through unwrapped
type MetadataSourceItem = PendingSection<
  Omit<MetadataSource, 'implementationName' | 'fields'>
> & {
  implementationName: string;
  fields: (Field & {
    pending: boolean;
    errors: object[];
    warnings: object[];
  })[];
  message?: Pending<{ type: string; message: string }>;
};

interface EditMetadataSourceModalContentProps {
  id?: number;
  onModalClose: () => void;
  onDeleteMetadataSourcePress?: () => void;
}

function EditMetadataSourceModalContent(
  props: EditMetadataSourceModalContentProps
) {
  const { id, onModalClose, onDeleteMetadataSourcePress } = props;

  const dispatch = useDispatch();

  const advancedSettings = useSelector(
    (state: AppState) => state.settings.advancedSettings
  );

  const providerSettingsSelector = useMemo(
    () => createProviderSettingsSelector('metadataSources'),
    []
  );

  const {
    item,
    isFetching,
    error,
    isSaving,
    isTesting,
    saveError,
    validationErrors,
    validationWarnings,
  } = useSelector((state: AppState) => providerSettingsSelector(state, { id }));

  const source = item as MetadataSourceItem;

  const wasSaving = usePrevious(isSaving);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [wasSaving, isSaving, saveError, onModalClose]);

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      // @ts-expect-error - actions are not typed
      dispatch(setMetadataSourceValue({ name, value }));
    },
    [dispatch]
  );

  const onFieldChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      // @ts-expect-error - actions are not typed
      dispatch(setMetadataSourceFieldValue({ name, value }));
    },
    [dispatch]
  );

  const onSavePress = useCallback(() => {
    dispatch(saveMetadataSource({ id }));
  }, [dispatch, id]);

  const onTestPress = useCallback(() => {
    dispatch(testMetadataSource({ id }));
  }, [dispatch, id]);

  const onAdvancedSettingsPress = useCallback(() => {
    dispatch(toggleAdvancedSettings());
  }, [dispatch]);

  const { implementationName, fields = [] } = source;

  const providerProps = {
    provider: 'metadataSource',
    providerData: item,
    section: 'settings.metadataSources',
  };

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id
          ? translate('EditMetadataSourceImplementation', {
              implementationName,
            })
          : translate('AddMetadataSourceImplementation', {
              implementationName,
            })}
      </ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && !!error ? (
          <Alert kind={kinds.DANGER}>
            {translate('AddMetadataSourceError')}
          </Alert>
        ) : null}

        {!isFetching && !error ? (
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {source.message ? (
              <Alert
                className={styles.message}
                kind={source.message.value.type}
              >
                {source.message.value.message}
              </Alert>
            ) : null}

            <FormGroup>
              <FormLabel>{translate('Name')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="name"
                {...source.name}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('Enable')}</FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="enable"
                helpText={translate('MetadataSourceEnableHelpText')}
                {...source.enable}
                onChange={onInputChange}
              />
            </FormGroup>

            {fields.length ? (
              <FieldSet legend={translate('MetadataSourceSettings')}>
                {fields.map((field) => (
                  <ProviderFieldFormGroup
                    key={field.name}
                    advancedSettings={advancedSettings}
                    {...providerProps}
                    {...field}
                    onChange={onFieldChange}
                  />
                ))}
              </FieldSet>
            ) : null}
          </Form>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteMetadataSourcePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <AdvancedSettingsButton
          advancedSettings={advancedSettings}
          onAdvancedSettingsPress={onAdvancedSettingsPress}
          showLabel={false}
        />

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={saveError}
          onPress={onTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={onSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditMetadataSourceModalContent;
