import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import tagExistingOptions from 'Settings/ImportLists/ImportLists/tagExistingOptions';
import { TagExisting } from 'typings/ImportList';
import translate from 'Utilities/String/translate';
import styles from './ManageImportListsEditModalContent.css';

interface SavePayload {
  enableAutomaticAdd?: boolean;
  qualityProfileId?: number;
  rootFolderPath?: string;
  tagExisting?: TagExisting;
}

interface ManageImportListsEditModalContentProps {
  importListIds: number[];
  onSavePress(payload: object): void;
  onModalClose(): void;
}

const NO_CHANGE = 'noChange';

const autoAddOptions = [
  {
    key: NO_CHANGE,
    get value() {
      return translate('NoChange');
    },
    isDisabled: true,
  },
  {
    key: 'enabled',
    get value() {
      return translate('Enabled');
    },
  },
  {
    key: 'disabled',
    get value() {
      return translate('Disabled');
    },
  },
];

const tagExistingWithNoChangeOptions = [
  {
    key: NO_CHANGE,
    get value() {
      return translate('NoChange');
    },
    isDisabled: true,
  },
  ...tagExistingOptions,
];

function ManageImportListsEditModalContent(
  props: ManageImportListsEditModalContentProps
) {
  const { importListIds, onSavePress, onModalClose } = props;

  const [enableAutomaticAdd, setEnableAutomaticAdd] = useState(NO_CHANGE);
  const [qualityProfileId, setQualityProfileId] = useState<string | number>(
    NO_CHANGE
  );
  const [rootFolderPath, setRootFolderPath] = useState(NO_CHANGE);
  const [tagExisting, setTagExisting] = useState<
    TagExisting | typeof NO_CHANGE
  >(NO_CHANGE);

  const save = useCallback(() => {
    let hasChanges = false;
    const payload: SavePayload = {};

    if (enableAutomaticAdd !== NO_CHANGE) {
      hasChanges = true;
      payload.enableAutomaticAdd = enableAutomaticAdd === 'enabled';
    }

    if (qualityProfileId !== NO_CHANGE) {
      hasChanges = true;
      payload.qualityProfileId = qualityProfileId as number;
    }

    if (rootFolderPath !== NO_CHANGE) {
      hasChanges = true;
      payload.rootFolderPath = rootFolderPath;
    }

    if (tagExisting !== NO_CHANGE) {
      hasChanges = true;
      payload.tagExisting = tagExisting;
    }

    if (hasChanges) {
      onSavePress(payload);
    }

    onModalClose();
  }, [
    enableAutomaticAdd,
    qualityProfileId,
    rootFolderPath,
    tagExisting,
    onSavePress,
    onModalClose,
  ]);

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: string }) => {
      switch (name) {
        case 'enableAutomaticAdd':
          setEnableAutomaticAdd(value);
          break;
        case 'qualityProfileId':
          setQualityProfileId(value);
          break;
        case 'rootFolderPath':
          setRootFolderPath(value);
          break;
        case 'tagExisting':
          setTagExisting(value as TagExisting);
          break;
        default:
          console.warn(`EditImportListModalContent Unknown Input: '${name}'`);
      }
    },
    []
  );

  const selectedCount = importListIds.length;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EditSelectedImportLists')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('AutomaticAdd')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="enableAutomaticAdd"
            value={enableAutomaticAdd}
            values={autoAddOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('QualityProfile')}</FormLabel>

          <FormInputGroup
            type={inputTypes.QUALITY_PROFILE_SELECT}
            name="qualityProfileId"
            value={qualityProfileId}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('RootFolder')}</FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            value={rootFolderPath}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            selectedValueOptions={{ includeFreeSpace: false }}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('TagExisting')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="tagExisting"
            value={tagExisting}
            values={tagExistingWithNoChangeOptions}
            onChange={onInputChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.selected}>
          {translate('CountImportListsSelected', { selectedCount })}
        </div>

        <div>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <Button onPress={save}>{translate('ApplyChanges')}</Button>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

export default ManageImportListsEditModalContent;
