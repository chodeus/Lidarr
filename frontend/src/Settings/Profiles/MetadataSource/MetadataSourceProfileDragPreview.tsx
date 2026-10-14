import React from 'react';
import { useDragLayer } from 'react-dnd';
import DragPreviewLayer from 'Components/DragPreviewLayer';
import { METADATA_SOURCE_PROFILE } from 'Helpers/dragTypes';
import dimensions from 'Styles/Variables/dimensions';
import MetadataSourceProfile from './MetadataSourceProfile';
import { MetadataSourceProfileDragSourceProps } from './MetadataSourceProfileDragSource';
import styles from './MetadataSourceProfileDragPreview.css';

const dragHandleWidth = parseInt(dimensions.dragHandleWidth);

interface MetadataSourceProfileDragPreviewProps {
  width: number;
}

function MetadataSourceProfileDragPreview({
  width,
}: MetadataSourceProfileDragPreviewProps) {
  const { item, itemType, currentOffset } = useDragLayer((monitor) => ({
    item: monitor.getItem() as MetadataSourceProfileDragSourceProps | null,
    itemType: monitor.getItemType(),
    currentOffset: monitor.getSourceClientOffset(),
  }));

  if (!item || !currentOffset || itemType !== METADATA_SOURCE_PROFILE) {
    return null;
  }

  // The drag handle sits at the row's right edge, so the preview shifts left by the rest of the row
  const handleOffset = width - dragHandleWidth;
  const transform = `translate3d(${currentOffset.x - handleOffset}px, ${
    currentOffset.y
  }px, 0)`;

  return (
    <DragPreviewLayer>
      <div
        className={styles.dragPreview}
        style={{ width, position: 'absolute', transform }}
      >
        <MetadataSourceProfile {...item} isDragging={false} />
      </div>
    </DragPreviewLayer>
  );
}

export default MetadataSourceProfileDragPreview;
