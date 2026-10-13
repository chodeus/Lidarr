import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import getMusicBrainzUrl from 'Utilities/getMusicBrainzUrl';
import styles from './AlbumDetailsLinks.css';

function AlbumDetailsLinks(props) {
  const {
    foreignAlbumId,
    links
  } = props;

  const musicBrainzUrl = getMusicBrainzUrl('release-group', foreignAlbumId);

  return (
    <div className={styles.links}>

      {musicBrainzUrl ? (
        <Link
          className={styles.link}
          to={musicBrainzUrl}
        >
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            Musicbrainz
          </Label>
        </Link>
      ) : null}

      {links.map((link, index) => {
        return (
          <span key={index}>
            <Link className={styles.link}
              to={link.url}
              key={index}
            >
              <Label
                className={styles.linkLabel}
                kind={kinds.INFO}
                size={sizes.LARGE}
              >
                {link.name}
              </Label>
            </Link>
            {(index > 0 && index % 5 === 0) &&
              <br />
            }

          </span>
        );
      })}

    </div>

  );
}

AlbumDetailsLinks.propTypes = {
  foreignAlbumId: PropTypes.string.isRequired,
  links: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default AlbumDetailsLinks;
