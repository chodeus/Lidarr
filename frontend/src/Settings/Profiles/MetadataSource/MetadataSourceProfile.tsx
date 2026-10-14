import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import { Tag } from 'App/State/TagsAppState';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { icons, kinds } from 'Helpers/Props';
import {
  MetadataSourceCombination,
  MetadataSourceProfileItem,
} from 'typings/MetadataSourceProfile';
import translate from 'Utilities/String/translate';
import EditMetadataSourceProfileModal from './EditMetadataSourceProfileModal';
import styles from './MetadataSourceProfile.css';

export interface MetadataSourceProfileProps {
  id: number;
  name: string;
  tags: number[];
  primaryCombination: MetadataSourceCombination;
  items: MetadataSourceProfileItem[];
  sourceNames: Record<number, string>;
  tagList?: Tag[];
  isDragging: boolean;
  connectDragSource?: (node: React.ReactElement) => React.ReactElement | null;
  onConfirmDeleteMetadataSourceProfile?: (id: number) => void;
}

function MetadataSourceProfile(props: MetadataSourceProfileProps) {
  const {
    id,
    name,
    tags,
    primaryCombination,
    items,
    sourceNames,
    tagList = [],
    isDragging,
    connectDragSource = (node: React.ReactElement) => node,
    onConfirmDeleteMetadataSourceProfile,
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
    onConfirmDeleteMetadataSourceProfile?.(id);
  }, [id, onConfirmDeleteMetadataSourceProfile]);

  const primaryCount = items.filter((item) => item.role === 'primary').length;

  return (
    <div
      className={classNames(
        styles.metadataSourceProfile,
        isDragging && styles.isDragging
      )}
    >
      <div className={styles.name}>{name}</div>

      <div className={styles.sources}>
        {primaryCount > 1 ? (
          <Label kind={kinds.DEFAULT} outline={true}>
            {primaryCombination === 'and'
              ? translate('MetadataSourceCombinationAnd')
              : translate('MetadataSourceCombinationOr')}
          </Label>
        ) : null}

        {items.map((item) => {
          const isPrimary = item.role === 'primary';

          return (
            <Label
              key={item.metadataSourceId}
              kind={isPrimary ? kinds.INFO : kinds.DEFAULT}
              title={
                isPrimary
                  ? translate('MetadataSourceRolePrimary')
                  : translate('MetadataSourceRoleFallback')
              }
            >
              {sourceNames[item.metadataSourceId] ?? item.metadataSourceId}
            </Label>
          );
        })}
      </div>

      <TagList className={styles.fillcolumn} tags={tags} tagList={tagList} />

      <div className={styles.actions}>
        <Link
          className={id === 1 ? styles.editButton : undefined}
          onPress={onEditPress}
        >
          <Icon name={icons.EDIT} />
        </Link>

        {id !== 1 &&
          connectDragSource(
            <div className={styles.dragHandle}>
              <Icon className={styles.dragIcon} name={icons.REORDER} />
            </div>
          )}
      </div>

      <EditMetadataSourceProfileModal
        id={id}
        isOpen={isEditModalOpen}
        onModalClose={onEditModalClose}
        onDeleteMetadataSourceProfilePress={onDeletePress}
      />

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteMetadataSourceProfile')}
        message={translate('DeleteMetadataSourceProfileMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={onConfirmDelete}
        onCancel={onDeleteModalClose}
      />
    </div>
  );
}

export default MetadataSourceProfile;
