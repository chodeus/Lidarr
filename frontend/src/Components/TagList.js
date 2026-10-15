import PropTypes from 'prop-types';
import React from 'react';
import { kinds } from 'Helpers/Props';
import sortByProp from 'Utilities/Array/sortByProp';
import Label from './Label';
import styles from './TagList.css';

function TagList({ className, tags, tagList, kind }) {
  const sortedTags = tags
    .map((tagId) => tagList.find((tag) => tag.id === tagId))
    .filter((tag) => !!tag)
    .sort(sortByProp('label'));

  return (
    <div className={className}>
      {
        sortedTags.map((tag) => {
          return (
            <Label
              key={tag.id}
              kind={kind}
            >
              {tag.label}
            </Label>
          );
        })
      }
    </div>
  );
}

TagList.propTypes = {
  className: PropTypes.string.isRequired,
  tags: PropTypes.arrayOf(PropTypes.number).isRequired,
  tagList: PropTypes.arrayOf(PropTypes.object).isRequired,
  kind: PropTypes.oneOf(kinds.all)
};

TagList.defaultProps = {
  className: styles.tags,
  kind: kinds.INFO
};

export default TagList;
