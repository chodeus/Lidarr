import classNames from 'classnames';
import React, { useCallback, useRef } from 'react';
import { useDrag, useDrop } from 'react-dnd';
import { METADATA_SOURCE_PROFILE } from 'Helpers/dragTypes';
import MetadataSourceProfile, {
  MetadataSourceProfileProps,
} from './MetadataSourceProfile';
import styles from './MetadataSourceProfileDragSource.css';

export interface MetadataSourceProfileDragSourceProps
  extends Omit<MetadataSourceProfileProps, 'isDragging' | 'connectDragSource'> {
  index: number;
  isDraggingUp: boolean;
  isDraggingDown: boolean;
  onMetadataSourceProfileDragMove: (
    dragIndex: number,
    dropIndex: number
  ) => void;
  onMetadataSourceProfileDragEnd: (
    item: MetadataSourceProfileDragSourceProps,
    didDrop: boolean
  ) => void;
}

function MetadataSourceProfileDragSource(
  props: MetadataSourceProfileDragSourceProps
) {
  const {
    index,
    isDraggingUp,
    isDraggingDown,
    onMetadataSourceProfileDragMove,
    onMetadataSourceProfileDragEnd,
    ...profileProps
  } = props;

  const ref = useRef<HTMLDivElement>(null);

  const [{ isOver }, dropRef] = useDrop<
    MetadataSourceProfileDragSourceProps,
    void,
    { isOver: boolean }
  >({
    accept: METADATA_SOURCE_PROFILE,
    collect: (monitor) => ({ isOver: monitor.isOver() }),
    hover: (item, monitor) => {
      const dragIndex = item.index;
      const clientOffset = monitor.getClientOffset();

      if (!ref.current || !clientOffset || dragIndex === index) {
        return;
      }

      // Positions, not stored orders, so a gap left by a delete can't misplace the drop
      const rect = ref.current.getBoundingClientRect();
      const hoverMiddleY = (rect.bottom - rect.top) / 2;
      const hoverClientY = clientOffset.y - rect.top;

      if (dragIndex < index && hoverClientY > hoverMiddleY) {
        onMetadataSourceProfileDragMove(dragIndex, index + 1);
      } else if (dragIndex > index && hoverClientY < hoverMiddleY) {
        onMetadataSourceProfileDragMove(dragIndex, index);
      }
    },
  });

  const [{ isDragging }, dragRef] = useDrag<
    MetadataSourceProfileDragSourceProps,
    void,
    { isDragging: boolean }
  >({
    type: METADATA_SOURCE_PROFILE,
    item: () => props,
    collect: (monitor) => ({ isDragging: monitor.isDragging() }),
    end: (item, monitor) => {
      onMetadataSourceProfileDragEnd(item, monitor.didDrop());
    },
  });

  const connectDragSource = useCallback(
    (node: React.ReactElement) => dragRef(node),
    [dragRef]
  );

  dropRef(ref);

  const isBefore = !isDragging && isDraggingUp && isOver;
  const isAfter = !isDragging && isDraggingDown && isOver;

  return (
    <div ref={ref} className={styles.metadataSourceProfileDragSource}>
      {isBefore ? (
        <div
          className={classNames(
            styles.metadataSourceProfilePlaceholder,
            styles.metadataSourceProfilePlaceholderBefore
          )}
        />
      ) : null}

      <MetadataSourceProfile
        {...profileProps}
        isDragging={isDragging}
        connectDragSource={connectDragSource}
      />

      {isAfter ? (
        <div
          className={classNames(
            styles.metadataSourceProfilePlaceholder,
            styles.metadataSourceProfilePlaceholderAfter
          )}
        />
      ) : null}
    </div>
  );
}

export default MetadataSourceProfileDragSource;
