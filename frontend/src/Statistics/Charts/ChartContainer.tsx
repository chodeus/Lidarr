import React from 'react';
import styles from './ChartContainer.css';

// The canvas is role="img": its name carries every label and value for screen readers
export function getChartAriaLabel(
  title: string,
  items: { label: string; value: number }[]
) {
  const values = items.map(
    (item) => `${item.label} ${item.value.toLocaleString()}`
  );

  return values.length ? `${title}: ${values.join(', ')}` : title;
}

interface ChartContainerProps {
  title: string;
  children: React.ReactNode;
}

function ChartContainer({ title, children }: ChartContainerProps) {
  return (
    <div className={styles.container}>
      <div className={styles.title}>{title}</div>

      <div className={styles.chart}>{children}</div>
    </div>
  );
}

export default ChartContainer;
