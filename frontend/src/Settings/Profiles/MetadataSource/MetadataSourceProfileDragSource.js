import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { DragSource, DropTarget } from 'react-dnd';
import { findDOMNode } from 'react-dom';
import { METADATA_SOURCE_PROFILE } from 'Helpers/dragTypes';
import MetadataSourceProfile from './MetadataSourceProfile';
import styles from './MetadataSourceProfileDragSource.css';

const metadataSourceProfileDragSource = {
  beginDrag(item) {
    return item;
  },

  endDrag(props, monitor, component) {
    props.onMetadataSourceProfileDragEnd(monitor.getItem(), monitor.didDrop());
  }
};

const metadataSourceProfileDropTarget = {
  hover(props, monitor, component) {
    const dragIndex = monitor.getItem().order;
    const hoverIndex = props.order;

    const hoverBoundingRect = findDOMNode(component).getBoundingClientRect();
    const hoverMiddleY = (hoverBoundingRect.bottom - hoverBoundingRect.top) / 2;
    const clientOffset = monitor.getClientOffset();
    const hoverClientY = clientOffset.y - hoverBoundingRect.top;

    if (dragIndex === hoverIndex) {
      return;
    }

    // When moving up, only trigger if drag position is above 50% and
    // when moving down, only trigger if drag position is below 50%.
    // If we're moving down the hoverIndex needs to be increased
    // by one so it's ordered properly. Otherwise the hoverIndex will work.

    if (dragIndex < hoverIndex && hoverClientY > hoverMiddleY) {
      props.onMetadataSourceProfileDragMove(dragIndex, hoverIndex + 1);
    } else if (dragIndex > hoverIndex && hoverClientY < hoverMiddleY) {
      props.onMetadataSourceProfileDragMove(dragIndex, hoverIndex);
    }
  }
};

function collectDragSource(connect, monitor) {
  return {
    connectDragSource: connect.dragSource(),
    isDragging: monitor.isDragging()
  };
}

function collectDropTarget(connect, monitor) {
  return {
    connectDropTarget: connect.dropTarget(),
    isOver: monitor.isOver()
  };
}

class MetadataSourceProfileDragSource extends Component {

  //
  // Render

  render() {
    const {
      id,
      order,
      isDragging,
      isDraggingUp,
      isDraggingDown,
      isOver,
      connectDragSource,
      connectDropTarget,
      ...otherProps
    } = this.props;

    const isBefore = !isDragging && isDraggingUp && isOver;
    const isAfter = !isDragging && isDraggingDown && isOver;

    return connectDropTarget(
      <div
        className={classNames(
          styles.metadataSourceProfileDragSource,
          isBefore && styles.isDraggingUp,
          isAfter && styles.isDraggingDown
        )}
      >
        {
          isBefore &&
            <div
              className={classNames(
                styles.metadataSourceProfilePlaceholder,
                styles.metadataSourceProfilePlaceholderBefore
              )}
            />
        }

        <MetadataSourceProfile
          id={id}
          order={order}
          isDragging={isDragging}
          {...otherProps}
          connectDragSource={connectDragSource}
        />

        {
          isAfter &&
            <div
              className={classNames(
                styles.metadataSourceProfilePlaceholder,
                styles.metadataSourceProfilePlaceholderAfter
              )}
            />
        }
      </div>
    );
  }
}

MetadataSourceProfileDragSource.propTypes = {
  id: PropTypes.number.isRequired,
  order: PropTypes.number.isRequired,
  isDragging: PropTypes.bool,
  isDraggingUp: PropTypes.bool,
  isDraggingDown: PropTypes.bool,
  isOver: PropTypes.bool,
  connectDragSource: PropTypes.func,
  connectDropTarget: PropTypes.func,
  onMetadataSourceProfileDragMove: PropTypes.func.isRequired,
  onMetadataSourceProfileDragEnd: PropTypes.func.isRequired
};

export default DropTarget(
  METADATA_SOURCE_PROFILE,
  metadataSourceProfileDropTarget,
  collectDropTarget
)(DragSource(
  METADATA_SOURCE_PROFILE,
  metadataSourceProfileDragSource,
  collectDragSource
)(MetadataSourceProfileDragSource));
