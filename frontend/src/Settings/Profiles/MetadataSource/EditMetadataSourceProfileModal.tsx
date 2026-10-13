import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditMetadataSourceProfileModalContent from './EditMetadataSourceProfileModalContent';

interface EditMetadataSourceProfileModalProps {
  id?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteMetadataSourceProfilePress?: () => void;
}

function EditMetadataSourceProfileModal(
  props: EditMetadataSourceProfileModalProps
) {
  const { id, isOpen, onModalClose, onDeleteMetadataSourceProfilePress } =
    props;

  const dispatch = useDispatch();

  const onModalCloseWrapper = useCallback(() => {
    dispatch(
      clearPendingChanges({ section: 'settings.metadataSourceProfiles' })
    );
    onModalClose();
  }, [dispatch, onModalClose]);

  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalCloseWrapper}
    >
      <EditMetadataSourceProfileModalContent
        id={id}
        onModalClose={onModalCloseWrapper}
        onDeleteMetadataSourceProfilePress={onDeleteMetadataSourceProfilePress}
      />
    </Modal>
  );
}

export default EditMetadataSourceProfileModal;
