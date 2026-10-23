import React, { FocusEvent, useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import ClipboardButton from 'Components/Link/ClipboardButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { icons, inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import General from 'typings/Settings/General';
import translate from 'Utilities/String/translate';
import AuthenticationMethodSettings from './AuthenticationMethodSettings';

export const authenticationMethodOptions = [
  {
    key: 'none',
    get value() {
      return translate('None');
    },
    isDisabled: true,
  },
  {
    key: 'external',
    get value() {
      return translate('External');
    },
    isHidden: true,
  },
  {
    key: 'basic',
    get value() {
      return translate('AuthBasic');
    },
    isDisabled: true,
    isHidden: true,
  },
  {
    key: 'forms',
    get value() {
      return translate('AuthForm');
    },
  },
  {
    key: 'oidc',
    get value() {
      return translate('Oidc');
    },
  },
];

export const authenticationRequiredOptions = [
  {
    key: 'enabled',
    get value() {
      return translate('Enabled');
    },
  },
  {
    key: 'disabledForLocalAddresses',
    get value() {
      return translate('DisabledForLocalAddresses');
    },
  },
];

const certificateValidationOptions = [
  {
    key: 'enabled',
    get value() {
      return translate('Enabled');
    },
  },
  {
    key: 'disabledForLocalAddresses',
    get value() {
      return translate('DisabledForLocalAddresses');
    },
  },
  {
    key: 'disabled',
    get value() {
      return translate('Disabled');
    },
  },
];

interface SecuritySettingsProps {
  settings: PendingSection<General>;
  isResettingApiKey: boolean;
  onInputChange: (change: InputChanged) => void;
  onConfirmResetApiKey: () => void;
}

function SecuritySettings({
  settings,
  isResettingApiKey,
  onInputChange,
  onConfirmResetApiKey,
}: SecuritySettingsProps) {
  const {
    authenticationMethod,
    authenticationRequired,
    username,
    password,
    passwordConfirmation,
    oidcAuthority,
    oidcClientId,
    oidcClientSecret,
    oidcUserIdentifier,
    oidcScopes,
    apiKey,
    certificateValidation,
    trustedNetworks,
  } = settings;

  const [isConfirmApiKeyResetModalOpen, setIsConfirmApiKeyResetModalOpen] =
    useState(false);

  const handleApikeyFocus = useCallback(
    (event: FocusEvent<HTMLInputElement, Element>) => {
      event.target.select();
    },
    []
  );

  const handleResetApiKeyPress = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(true);
  }, []);

  const handleConfirmResetApiKey = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(false);
    onConfirmResetApiKey();
  }, [onConfirmResetApiKey]);

  const handleCloseResetApiKeyModal = useCallback(() => {
    setIsConfirmApiKeyResetModalOpen(false);
  }, []);

  const authenticationEnabled =
    authenticationMethod && authenticationMethod.value !== 'none';

  return (
    <FieldSet legend={translate('Security')}>
      <FormGroup>
        <FormLabel>{translate('Authentication')}</FormLabel>

        <FormInputGroup
          type={inputTypes.SELECT}
          name="authenticationMethod"
          values={authenticationMethodOptions}
          helpText={translate('AuthenticationMethodHelpText')}
          helpTextWarning={translate('AuthenticationRequiredWarning')}
          onChange={onInputChange}
          {...authenticationMethod}
        />
      </FormGroup>

      {authenticationEnabled ? (
        <FormGroup>
          <FormLabel>{translate('AuthenticationRequired')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="authenticationRequired"
            values={authenticationRequiredOptions}
            helpText={translate('AuthenticationRequiredHelpText')}
            onChange={onInputChange}
            {...authenticationRequired}
          />
        </FormGroup>
      ) : null}

      <AuthenticationMethodSettings
        authenticationMethod={authenticationMethod}
        username={username}
        password={password}
        passwordConfirmation={passwordConfirmation}
        oidcAuthority={oidcAuthority}
        oidcClientId={oidcClientId}
        oidcClientSecret={oidcClientSecret}
        oidcUserIdentifier={oidcUserIdentifier}
        oidcScopes={oidcScopes}
        onInputChange={onInputChange}
      />

      <FormGroup>
        <FormLabel>{translate('APIKey')}</FormLabel>

        <FormInputGroup
          type={inputTypes.TEXT}
          name="apiKey"
          readOnly={true}
          helpTextWarning={translate('ApiKeyHelpTextWarning')}
          buttons={[
            <ClipboardButton
              key="copy"
              value={apiKey.value}
              kind={kinds.DEFAULT}
            />,

            <FormInputButton
              key="reset"
              kind={kinds.DANGER}
              onPress={handleResetApiKeyPress}
            >
              <Icon name={icons.REFRESH} isSpinning={isResettingApiKey} />
            </FormInputButton>,
          ]}
          onChange={onInputChange}
          onFocus={handleApikeyFocus}
          {...apiKey}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('CertificateValidation')}</FormLabel>

        <FormInputGroup
          type={inputTypes.SELECT}
          name="certificateValidation"
          values={certificateValidationOptions}
          helpText={translate('CertificateValidationHelpText')}
          onChange={onInputChange}
          {...certificateValidation}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('TrustedNetworks')}</FormLabel>

        <FormInputGroup
          type={inputTypes.TEXT}
          name="trustedNetworks"
          helpText={translate('TrustedNetworksHelpText')}
          helpTextWarning={translate('RestartRequiredHelpTextWarning')}
          helpLink="https://wiki.servarr.com/lidarr/settings#security"
          onChange={onInputChange}
          {...trustedNetworks}
        />
      </FormGroup>

      <ConfirmModal
        isOpen={isConfirmApiKeyResetModalOpen}
        kind={kinds.DANGER}
        title={translate('ResetAPIKey')}
        message={translate('ResetAPIKeyMessageText')}
        confirmLabel={translate('Reset')}
        onConfirm={handleConfirmResetApiKey}
        onCancel={handleCloseResetApiKeyModal}
      />
    </FieldSet>
  );
}

export default SecuritySettings;
