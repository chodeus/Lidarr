import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import {
  fetchMetadataSourceProfileSchema,
  fetchMetadataSources,
  saveMetadataSourceProfile,
  setMetadataSourceProfileValue,
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import MetadataSourceProfile, {
  MetadataSourceProfileItem,
  MetadataSourceRole,
} from 'typings/MetadataSourceProfile';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import styles from './EditMetadataSourceProfileModalContent.css';

interface EditMetadataSourceProfileModalContentProps {
  id?: number;
  onModalClose: () => void;
  onDeleteMetadataSourceProfilePress?: () => void;
}

function EditMetadataSourceProfileModalContent(
  props: EditMetadataSourceProfileModalContentProps
) {
  const { id, onModalClose, onDeleteMetadataSourceProfilePress } = props;

  const dispatch = useDispatch();

  const providerSettingsSelector = useMemo(
    () => createProviderSettingsSelector('metadataSourceProfiles'),
    []
  );

  const {
    item,
    isFetching,
    isPopulated,
    error,
    isSaving,
    saveError,
    validationErrors,
    validationWarnings,
  } = useSelector((state: AppState) => providerSettingsSelector(state, { id }));

  const sources = useSelector(
    (state: AppState) => state.settings.metadataSources.items
  );

  const profile = item as PendingSection<MetadataSourceProfile>;
  const items = useMemo(
    () => (profile.items?.value ?? []) as MetadataSourceProfileItem[],
    [profile.items?.value]
  );
  const wasSaving = usePrevious(isSaving);

  const [newSourceId, setNewSourceId] = useState<number | null>(null);

  useEffect(() => {
    dispatch(fetchMetadataSources());

    if (!id && !isPopulated) {
      dispatch(fetchMetadataSourceProfileSchema());
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [wasSaving, isSaving, saveError, onModalClose]);

  const combinationOptions = useMemo(() => {
    return [
      { key: 'or', value: translate('MetadataSourceCombinationOr') },
      { key: 'and', value: translate('MetadataSourceCombinationAnd') },
    ];
  }, []);

  const roleOptions = useMemo(() => {
    return [
      { key: 'primary', value: translate('MetadataSourceRolePrimary') },
      { key: 'fallback', value: translate('MetadataSourceRoleFallback') },
    ];
  }, []);

  const addableSources = useMemo(() => {
    return sources
      .filter(
        (source) =>
          !items.some(
            (profileItem) => profileItem.metadataSourceId === source.id
          )
      )
      .map((source) => ({ key: source.id, value: source.name }));
  }, [sources, items]);

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      // @ts-expect-error - actions are not typed
      dispatch(setMetadataSourceProfileValue({ name, value }));
    },
    [dispatch]
  );

  const setItems = useCallback(
    (newItems: MetadataSourceProfileItem[]) => {
      onInputChange({ name: 'items', value: newItems });
    },
    [onInputChange]
  );

  const onRoleChange = useCallback(
    (index: number, role: MetadataSourceRole) => {
      setItems(
        items.map((profileItem, i) =>
          i === index ? { ...profileItem, role } : profileItem
        )
      );
    },
    [items, setItems]
  );

  const onMove = useCallback(
    (index: number, offset: number) => {
      const newItems = [...items];
      const [moving] = newItems.splice(index, 1);
      newItems.splice(index + offset, 0, moving);
      setItems(newItems);
    },
    [items, setItems]
  );

  const onRemove = useCallback(
    (index: number) => {
      setItems(items.filter((_profileItem, i) => i !== index));
    },
    [items, setItems]
  );

  const onAddSourcePress = useCallback(() => {
    const sourceId = newSourceId ?? addableSources[0]?.key;

    if (sourceId == null) {
      return;
    }

    setItems([...items, { metadataSourceId: sourceId, role: 'fallback' }]);
    setNewSourceId(null);
  }, [newSourceId, addableSources, items, setItems]);

  const onSavePress = useCallback(() => {
    dispatch(saveMetadataSourceProfile({ id }));
  }, [dispatch, id]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id
          ? translate('EditMetadataSourceProfile')
          : translate('AddMetadataSourceProfile')}
      </ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && !!error ? (
          <Alert kind={kinds.DANGER}>
            {translate('AddMetadataSourceProfileError')}
          </Alert>
        ) : null}

        {!isFetching && isPopulated && !error ? (
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            <FormGroup size={sizes.SMALL}>
              <FormLabel size={sizes.SMALL}>{translate('Name')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="name"
                {...profile.name}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('MetadataSourceCombination')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="primaryCombination"
                values={combinationOptions}
                helpText={translate('MetadataSourceCombinationHelpText')}
                {...profile.primaryCombination}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('MetadataSources')}</FormLabel>

              <div className={styles.sources}>
                {items.map((profileItem, index) => {
                  const source = sources.find(
                    (s) => s.id === profileItem.metadataSourceId
                  );

                  return (
                    <div
                      key={profileItem.metadataSourceId}
                      className={styles.source}
                    >
                      <div className={styles.sourceName}>
                        {source?.name ?? profileItem.metadataSourceId}
                      </div>

                      <div className={styles.sourceRole}>
                        <FormInputGroup
                          type={inputTypes.SELECT}
                          name={`role-${index}`}
                          values={roleOptions}
                          value={profileItem.role}
                          onChange={({
                            value,
                          }: {
                            value: MetadataSourceRole;
                          }) => onRoleChange(index, value)}
                        />
                      </div>

                      <IconButton
                        name={icons.ARROW_UP}
                        title={translate('MoveUp')}
                        isDisabled={index === 0}
                        onPress={() => onMove(index, -1)}
                      />

                      <IconButton
                        name={icons.ARROW_DOWN}
                        title={translate('MoveDown')}
                        isDisabled={index === items.length - 1}
                        onPress={() => onMove(index, 1)}
                      />

                      <IconButton
                        name={icons.REMOVE}
                        title={translate('Remove')}
                        onPress={() => onRemove(index)}
                      />
                    </div>
                  );
                })}

                {addableSources.length ? (
                  <div className={styles.addSource}>
                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="newSourceId"
                      values={addableSources}
                      value={newSourceId ?? addableSources[0].key}
                      onChange={({ value }: { value: number }) =>
                        setNewSourceId(Number(value))
                      }
                    />

                    <Button size={sizes.SMALL} onPress={onAddSourcePress}>
                      {translate('AddMetadataSourceToProfile')}
                    </Button>
                  </div>
                ) : null}

                <Alert kind={kinds.INFO}>
                  {translate('MetadataSourceRolesHelpText')}
                </Alert>
              </div>
            </FormGroup>

            {id === 1 ? (
              <Alert>{translate('DefaultMetadataSourceProfileMessage')}</Alert>
            ) : (
              <FormGroup size={sizes.SMALL}>
                <FormLabel size={sizes.SMALL}>{translate('Tags')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TAG}
                  name="tags"
                  helpText={translate('MetadataSourceProfileTagsHelpText')}
                  {...profile.tags}
                  onChange={onInputChange}
                />
              </FormGroup>
            )}
          </Form>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {id && id > 1 ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteMetadataSourceProfilePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

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

export default EditMetadataSourceProfileModalContent;
