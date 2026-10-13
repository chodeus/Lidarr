import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import {
  deleteMetadataSource,
  fetchMetadataSources,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import AddMetadataSourceModal from './AddMetadataSourceModal';
import EditMetadataSourceModal from './EditMetadataSourceModal';
import MetadataSource from './MetadataSource';
import styles from './MetadataSources.css';

function MetadataSources() {
  const dispatch = useDispatch();

  const { isFetching, isPopulated, error, items } = useSelector(
    (state: AppState) => state.settings.metadataSources
  );

  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);

  useEffect(() => {
    dispatch(fetchMetadataSources());
  }, [dispatch]);

  const onAddPress = useCallback(() => {
    setIsAddModalOpen(true);
  }, []);

  const onAddModalClose = useCallback(
    ({ sourceSelected = false }: { sourceSelected?: boolean } = {}) => {
      setIsAddModalOpen(false);
      setIsEditModalOpen(sourceSelected);
    },
    []
  );

  const onEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
  }, []);

  const onConfirmDeleteMetadataSource = useCallback(
    (id: number) => {
      dispatch(deleteMetadataSource({ id }));
    },
    [dispatch]
  );

  return (
    <FieldSet legend={translate('MetadataSources')}>
      <PageSectionContent
        errorMessage={translate('MetadataSourcesLoadError')}
        isFetching={isFetching}
        isPopulated={isPopulated}
        error={error}
      >
        <div className={styles.sources}>
          {items.map((item) => (
            <MetadataSource
              key={item.id}
              {...item}
              onConfirmDeleteMetadataSource={onConfirmDeleteMetadataSource}
            />
          ))}

          <Card className={styles.addSource} onPress={onAddPress}>
            <div className={styles.center}>
              <Icon name={icons.ADD} size={45} />
            </div>
          </Card>
        </div>

        <AddMetadataSourceModal
          isOpen={isAddModalOpen}
          onModalClose={onAddModalClose}
        />

        <EditMetadataSourceModal
          isOpen={isEditModalOpen}
          onModalClose={onEditModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default MetadataSources;
