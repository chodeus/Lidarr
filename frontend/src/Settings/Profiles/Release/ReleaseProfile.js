import PropTypes from 'prop-types';
import React, { Component } from 'react';
import MiddleTruncate from 'react-middle-truncate';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditReleaseProfileModalConnector from './EditReleaseProfileModalConnector';
import styles from './ReleaseProfile.css';

class ReleaseProfile extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isEditReleaseProfileModalOpen: false,
      isDeleteReleaseProfileModalOpen: false
    };
  }

  //
  // Listeners

  onEditReleaseProfilePress = () => {
    this.setState({ isEditReleaseProfileModalOpen: true });
  };

  onEditReleaseProfileModalClose = () => {
    this.setState({ isEditReleaseProfileModalOpen: false });
  };

  onDeleteReleaseProfilePress = () => {
    this.setState({
      isEditReleaseProfileModalOpen: false,
      isDeleteReleaseProfileModalOpen: true
    });
  };

  onDeleteReleaseProfileModalClose= () => {
    this.setState({ isDeleteReleaseProfileModalOpen: false });
  };

  onConfirmDeleteReleaseProfile = () => {
    this.props.onConfirmDeleteReleaseProfile(this.props.id);
  };

  //
  // Render

  render() {
    const {
      id,
      enabled,
      required,
      ignored,
      tags,
      excludedTags,
      indexerIds,
      tagList,
      indexerList
    } = this.props;

    const {
      isEditReleaseProfileModalOpen,
      isDeleteReleaseProfileModalOpen
    } = this.state;

    const indexers = indexerList.filter((indexer) => indexerIds.includes(indexer.id));

    return (
      <Card
        className={styles.releaseProfile}
        overlayContent={true}
        onPress={this.onEditReleaseProfilePress}
      >
        <div>
          {
            required.map((item) => {
              if (!item) {
                return null;
              }

              return (
                <Label
                  className={styles.label}
                  key={item}
                  kind={kinds.SUCCESS}
                >
                  <MiddleTruncate
                    text={item}
                    start={10}
                    end={10}
                  />
                </Label>
              );
            })
          }
        </div>

        <div>
          {
            ignored.map((item) => {
              if (!item) {
                return null;
              }

              return (
                <Label
                  className={styles.label}
                  key={item}
                  kind={kinds.DANGER}
                >
                  <MiddleTruncate
                    text={item}
                    start={10}
                    end={10}
                  />
                </Label>
              );
            })
          }
        </div>

        <TagList
          tags={tags}
          tagList={tagList}
        />

        <TagList
          tags={excludedTags}
          tagList={tagList}
          kind={kinds.DANGER}
        />

        <div>
          {
            !enabled &&
              <Label
                kind={kinds.DISABLED}
                outline={true}
              >
                Disabled
              </Label>
          }

          {
            indexers.map((indexer) => {
              return (
                <Label
                  key={indexer.id}
                  kind={kinds.INFO}
                  outline={true}
                >
                  {indexer.name}
                </Label>
              );
            })
          }
        </div>

        <EditReleaseProfileModalConnector
          id={id}
          isOpen={isEditReleaseProfileModalOpen}
          onModalClose={this.onEditReleaseProfileModalClose}
          onDeleteReleaseProfilePress={this.onDeleteReleaseProfilePress}
        />

        <ConfirmModal
          isOpen={isDeleteReleaseProfileModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteReleaseProfile')}
          message={translate('DeleteReleaseProfileMessageText')}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDeleteReleaseProfile}
          onCancel={this.onDeleteReleaseProfileModalClose}
        />
      </Card>
    );
  }
}

ReleaseProfile.propTypes = {
  id: PropTypes.number.isRequired,
  enabled: PropTypes.bool.isRequired,
  required: PropTypes.arrayOf(PropTypes.string).isRequired,
  ignored: PropTypes.arrayOf(PropTypes.string).isRequired,
  tags: PropTypes.arrayOf(PropTypes.number).isRequired,
  excludedTags: PropTypes.arrayOf(PropTypes.number).isRequired,
  indexerIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  tagList: PropTypes.arrayOf(PropTypes.object).isRequired,
  indexerList: PropTypes.arrayOf(PropTypes.object).isRequired,
  onConfirmDeleteReleaseProfile: PropTypes.func.isRequired
};

ReleaseProfile.defaultProps = {
  enabled: true,
  required: [],
  ignored: [],
  excludedTags: [],
  indexerIds: []
};

export default ReleaseProfile;
