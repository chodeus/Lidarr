import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import AddMetadataSourceModalContent from './AddMetadataSourceModalContent';

interface AddMetadataSourceModalProps {
  isOpen: boolean;
  onModalClose: (options?: { sourceSelected?: boolean }) => void;
}

function AddMetadataSourceModal(props: AddMetadataSourceModalProps) {
  const { isOpen, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <AddMetadataSourceModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddMetadataSourceModal;
