import {
  BarElement,
  CategoryScale,
  Chart,
  ChartOptions,
  LinearScale,
  Tooltip,
  TooltipItem,
} from 'chart.js';
import React, { useMemo } from 'react';
import { Bar } from 'react-chartjs-2';
import useChartColors from 'Statistics/Charts/useChartColors';
import ChartContainer, { getChartAriaLabel } from './ChartContainer';

Chart.register(BarElement, CategoryScale, LinearScale, Tooltip);

export interface BarChartItem {
  label: string;
  value: number;
  tooltipLines?: string[];
}

interface BarChartProps {
  title: string;
  items: BarChartItem[];
  horizontal?: boolean;
}

// Horizontal bars get a row per label so long names are never skipped
const HORIZONTAL_ROW_HEIGHT = 24;

function BarChart({ title, items, horizontal = false }: BarChartProps) {
  const colors = useChartColors();

  const data = useMemo(() => {
    return {
      labels: items.map((item) => item.label),
      datasets: [
        {
          data: items.map((item) => item.value),
          backgroundColor: colors.bar,
          borderRadius: 3,
          maxBarThickness: 50,
        },
      ],
    };
  }, [colors, items]);

  const options = useMemo<ChartOptions<'bar'>>(() => {
    const categoryAxis = {
      ticks: {
        color: colors.text,
        autoSkip: !horizontal,
      },
      grid: {
        display: false,
      },
    };

    const valueAxis = {
      beginAtZero: true,
      ticks: {
        precision: 0,
        color: colors.text,
      },
      grid: {
        color: colors.grid,
      },
    };

    return {
      indexAxis: horizontal ? 'y' : 'x',
      responsive: true,
      maintainAspectRatio: false,
      scales: horizontal
        ? { x: valueAxis, y: categoryAxis }
        : { x: categoryAxis, y: valueAxis },
      plugins: {
        legend: {
          display: false,
        },
        tooltip: {
          callbacks: {
            label: (context: TooltipItem<'bar'>) => {
              const value = horizontal ? context.parsed.x : context.parsed.y;

              return ` ${(value ?? 0).toLocaleString()}`;
            },
            afterBody: (contexts: TooltipItem<'bar'>[]) => {
              const index = contexts[0]?.dataIndex ?? 0;

              return items[index]?.tooltipLines ?? [];
            },
          },
        },
      },
    };
  }, [colors, horizontal, items]);

  return (
    <ChartContainer
      title={title}
      height={horizontal ? items.length * HORIZONTAL_ROW_HEIGHT : undefined}
    >
      <Bar
        data={data}
        options={options}
        aria-label={getChartAriaLabel(title, items)}
      />
    </ChartContainer>
  );
}

export default BarChart;
