import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import {
  fetchMetadataSourceSchema,
  selectMetadataSourceSchema,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import styles from './AddMetadataSourceModalContent.css';

interface AddMetadataSourceModalContentProps {
  onModalClose: (options?: { sourceSelected?: boolean }) => void;
}

function AddMetadataSourceModalContent(
  props: AddMetadataSourceModalContentProps
) {
  const { onModalClose } = props;

  const dispatch = useDispatch();

  const { isSchemaFetching, isSchemaPopulated, schemaError, schema } =
    useSelector((state: AppState) => state.settings.metadataSources);

  useEffect(() => {
    dispatch(fetchMetadataSourceSchema());
  }, [dispatch]);

  const onSourceSelect = useCallback(
    ({
      implementation,
      implementationName,
    }: {
      implementation: string;
      implementationName: string;
    }) => {
      dispatch(
        selectMetadataSourceSchema({ implementation, implementationName })
      );
      onModalClose({ sourceSelected: true });
    },
    [dispatch, onModalClose]
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddMetadataSource')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>
            {translate('AddMetadataSourceError')}
          </Alert>
        ) : null}

        {isSchemaPopulated && !schemaError ? (
          <div className={styles.sources}>
            {schema.map((item) => (
              <div key={item.implementation} className={styles.source}>
                <Link
                  className={styles.underlay}
                  onPress={() => onSourceSelect(item)}
                />

                <div className={styles.overlay}>
                  <div className={styles.name}>{item.implementationName}</div>
                </div>
              </div>
            ))}
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={() => onModalClose()}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddMetadataSourceModalContent;
