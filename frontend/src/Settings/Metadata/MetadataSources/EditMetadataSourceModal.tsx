import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  cancelSaveMetadataSource,
  cancelTestMetadataSource,
} from 'Store/Actions/settingsActions';
import EditMetadataSourceModalContent from './EditMetadataSourceModalContent';

interface EditMetadataSourceModalProps {
  id?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteMetadataSourcePress?: () => void;
}

function EditMetadataSourceModal(props: EditMetadataSourceModalProps) {
  const { id, isOpen, onModalClose, onDeleteMetadataSourcePress } = props;

  const dispatch = useDispatch();

  const onModalCloseWrapper = useCallback(() => {
    dispatch(clearPendingChanges({ section: 'settings.metadataSources' }));
    dispatch(cancelTestMetadataSource({ section: 'settings.metadataSources' }));
    dispatch(cancelSaveMetadataSource({ section: 'settings.metadataSources' }));
    onModalClose();
  }, [dispatch, onModalClose]);

  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalCloseWrapper}
    >
      <EditMetadataSourceModalContent
        id={id}
        onModalClose={onModalCloseWrapper}
        onDeleteMetadataSourcePress={onDeleteMetadataSourcePress}
      />
    </Modal>
  );
}

export default EditMetadataSourceModal;
