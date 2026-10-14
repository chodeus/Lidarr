import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import Measure from 'Components/Measure';
import PageSectionContent from 'Components/Page/PageSectionContent';
import Scroller from 'Components/Scroller/Scroller';
import { icons } from 'Helpers/Props';
import ScrollDirection from 'Helpers/Props/ScrollDirection';
import {
  deleteMetadataSourceProfile,
  fetchMetadataSourceProfiles,
  fetchMetadataSources,
  reorderMetadataSourceProfile,
} from 'Store/Actions/settingsActions';
import createTagsSelector from 'Store/Selectors/createTagsSelector';
import translate from 'Utilities/String/translate';
import EditMetadataSourceProfileModal from './EditMetadataSourceProfileModal';
import MetadataSourceProfile from './MetadataSourceProfile';
import MetadataSourceProfileDragPreview from './MetadataSourceProfileDragPreview';
import MetadataSourceProfileDragSource from './MetadataSourceProfileDragSource';
import styles from './MetadataSourceProfiles.css';

function MetadataSourceProfiles() {
  const dispatch = useDispatch();

  const { isFetching, isPopulated, error, items } = useSelector(
    (state: AppState) => state.settings.metadataSourceProfiles
  );

  const sources = useSelector(
    (state: AppState) => state.settings.metadataSources.items
  );

  const tagList = useSelector(createTagsSelector());

  const sourceNames = useMemo(() => {
    return sources.reduce<Record<number, string>>((names, source) => {
      names[source.id] = source.name;
      return names;
    }, {});
  }, [sources]);

  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [width, setWidth] = useState(0);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);

  useEffect(() => {
    dispatch(fetchMetadataSourceProfiles());
    dispatch(fetchMetadataSources());
  }, [dispatch]);

  const defaultProfile = items.find((item) => item.id === 1);
  const profiles = items
    .filter((item) => item.id !== 1)
    .sort((a, b) => a.order - b.order);

  const onAddPress = useCallback(() => {
    setIsAddModalOpen(true);
  }, []);

  const onAddModalClose = useCallback(() => {
    setIsAddModalOpen(false);
  }, []);

  const onMeasure = useCallback(({ width: newWidth }: { width: number }) => {
    setWidth(newWidth);
  }, []);

  const onConfirmDeleteMetadataSourceProfile = useCallback(
    (id: number) => {
      dispatch(deleteMetadataSourceProfile({ id }));
    },
    [dispatch]
  );

  const onMetadataSourceProfileDragMove = useCallback(
    (newDragIndex: number, newDropIndex: number) => {
      setDragIndex(newDragIndex);
      setDropIndex(newDropIndex);
    },
    []
  );

  const onMetadataSourceProfileDragEnd = useCallback(
    ({ id }: { id: number }, didDrop: boolean) => {
      if (didDrop && dropIndex !== null) {
        dispatch(reorderMetadataSourceProfile({ id, moveIndex: dropIndex }));
      }

      setDragIndex(null);
      setDropIndex(null);
    },
    [dispatch, dropIndex]
  );

  const isDragging = dropIndex !== null;
  const isDraggingUp =
    isDragging && dragIndex !== null && dropIndex < dragIndex;
  const isDraggingDown =
    isDragging && dragIndex !== null && dropIndex > dragIndex;

  return (
    <Measure onMeasure={onMeasure}>
      <FieldSet legend={translate('MetadataSourceProfiles')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadMetadataSourceProfiles')}
          isFetching={isFetching}
          isPopulated={isPopulated}
          error={error}
        >
          <Scroller
            className={styles.horizontalScroll}
            scrollDirection={ScrollDirection.Horizontal}
            autoFocus={false}
          >
            <div>
              <div className={styles.metadataSourceProfilesHeader}>
                <div className={styles.name}>{translate('Name')}</div>
                <div className={styles.sources}>
                  {translate('MetadataSources')}
                </div>
                <div className={styles.fillcolumn}>{translate('Tags')}</div>
                <div className={styles.actions} />
              </div>

              <div className={styles.metadataSourceProfiles}>
                {profiles.map((item, index) => {
                  return (
                    <MetadataSourceProfileDragSource
                      key={item.id}
                      tagList={tagList}
                      sourceNames={sourceNames}
                      {...item}
                      index={index}
                      isDraggingUp={isDraggingUp}
                      isDraggingDown={isDraggingDown}
                      onConfirmDeleteMetadataSourceProfile={
                        onConfirmDeleteMetadataSourceProfile
                      }
                      onMetadataSourceProfileDragMove={
                        onMetadataSourceProfileDragMove
                      }
                      onMetadataSourceProfileDragEnd={
                        onMetadataSourceProfileDragEnd
                      }
                    />
                  );
                })}

                <MetadataSourceProfileDragPreview width={width} />
              </div>

              {defaultProfile ? (
                <div>
                  <MetadataSourceProfile
                    tagList={tagList}
                    sourceNames={sourceNames}
                    isDragging={false}
                    onConfirmDeleteMetadataSourceProfile={
                      onConfirmDeleteMetadataSourceProfile
                    }
                    {...defaultProfile}
                  />
                </div>
              ) : null}
            </div>
          </Scroller>

          <div className={styles.addMetadataSourceProfile}>
            <Link className={styles.addButton} onPress={onAddPress}>
              <Icon name={icons.ADD} />
            </Link>
          </div>

          <EditMetadataSourceProfileModal
            isOpen={isAddModalOpen}
            onModalClose={onAddModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    </Measure>
  );
}

export default MetadataSourceProfiles;
