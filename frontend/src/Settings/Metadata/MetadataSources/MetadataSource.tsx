import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditMetadataSourceModal from './EditMetadataSourceModal';
import styles from './MetadataSource.css';

interface MetadataSourceProps {
  id: number;
  name: string;
  enable: boolean;
  implementationName: string;
  onConfirmDeleteMetadataSource: (id: number) => void;
}

function MetadataSource(props: MetadataSourceProps) {
  const {
    id,
    name,
    enable,
    implementationName,
    onConfirmDeleteMetadataSource,
  } = props;

  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);

  const onEditPress = useCallback(() => {
    setIsEditModalOpen(true);
  }, []);

  const onEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
  }, []);

  const onDeletePress = useCallback(() => {
    setIsEditModalOpen(false);
    setIsDeleteModalOpen(true);
  }, []);

  const onDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, []);

  const onConfirmDelete = useCallback(() => {
    onConfirmDeleteMetadataSource(id);
  }, [id, onConfirmDeleteMetadataSource]);

  return (
    <Card className={styles.source} overlayContent={true} onPress={onEditPress}>
      <div className={styles.name}>{name}</div>

      <div className={styles.enabled}>
        <Label kind={kinds.DEFAULT}>{implementationName}</Label>

        {enable ? null : (
          <Label kind={kinds.DISABLED} outline={true}>
            {translate('Disabled')}
          </Label>
        )}
      </div>

      <EditMetadataSourceModal
        id={id}
        isOpen={isEditModalOpen}
        onModalClose={onEditModalClose}
        onDeleteMetadataSourcePress={onDeletePress}
      />

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteMetadataSource')}
        message={translate('DeleteMetadataSourceMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={onConfirmDelete}
        onCancel={onDeleteModalClose}
      />
    </Card>
  );
}

export default MetadataSource;
