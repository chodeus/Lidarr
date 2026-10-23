import classNames from 'classnames';
import React, { ReactNode } from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import styles from './FormInputButton.css';

interface FormInputButtonProps {
  className?: string;
  canSpin?: boolean;
  isLastButton?: boolean;
  isSpinning?: boolean;
  isDisabled?: boolean;
  kind?: string;
  title?: string;
  children?: ReactNode;
  onPress?: () => void;
}

function FormInputButton({
  className = styles.button,
  canSpin = false,
  isLastButton = true,
  kind = kinds.PRIMARY,
  ...otherProps
}: FormInputButtonProps) {
  if (canSpin) {
    return (
      <SpinnerButton
        className={classNames(className, !isLastButton && styles.middleButton)}
        kind={kind}
        {...otherProps}
      />
    );
  }

  return (
    <Button
      className={classNames(className, !isLastButton && styles.middleButton)}
      kind={kind}
      {...otherProps}
    />
  );
}

export default FormInputButton;
