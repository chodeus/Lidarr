import {
  ArcElement,
  Chart,
  ChartEvent,
  ChartOptions,
  Legend,
  LegendElement,
  LegendItem,
  Tooltip,
  TooltipItem,
} from 'chart.js';
import React, { useContext, useMemo } from 'react';
import { Doughnut } from 'react-chartjs-2';
import ColorImpairedContext from 'App/ColorImpairedContext';
import useChartColors from 'Statistics/Charts/useChartColors';
import ChartContainer from './ChartContainer';
import createSlicePattern from './createSlicePattern';

Chart.register(ArcElement, Tooltip, Legend);

export interface DoughnutChartItem {
  label: string;
  value: number;
  color: string;
}

interface DoughnutChartProps {
  title: string;
  items: DoughnutChartItem[];
}

function formatShare(value: number, total: number) {
  const percentage = total ? Math.round((value / total) * 100) : 0;

  if (percentage === 0 && value > 0) {
    return '<1%';
  }

  if (percentage === 100 && value < total) {
    return '>99%';
  }

  return `${percentage}%`;
}

function DoughnutChart({ title, items }: DoughnutChartProps) {
  const colors = useChartColors();
  const enableColorImpairedMode = useContext(ColorImpairedContext);

  const data = useMemo(() => {
    return {
      labels: items.map((item) => item.label),
      datasets: [
        {
          data: items.map((item) => item.value),
          backgroundColor: items.map((item, index) =>
            enableColorImpairedMode
              ? createSlicePattern(
                  item.color,
                  colors.colorImpairedStripe,
                  index
                )
              : item.color
          ),
          borderWidth: 0,
        },
      ],
    };
  }, [colors, enableColorImpairedMode, items]);

  const options = useMemo<ChartOptions<'doughnut'>>(() => {
    return {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '60%',
      plugins: {
        legend: {
          position: 'bottom',
          labels: {
            color: colors.text,
          },
          onHover: (
            event: ChartEvent,
            legendItem: LegendItem,
            legend: LegendElement<'doughnut'>
          ) => {
            if (legendItem.hidden) {
              return;
            }

            const chart = legend.chart;
            const index = legendItem.index ?? 0;
            const activeElements = [{ datasetIndex: 0, index }];

            chart.setActiveElements(activeElements);
            chart.tooltip?.setActiveElements(activeElements, {
              x: event.x ?? 0,
              y: event.y ?? 0,
            });
            chart.update();
          },
          onLeave: (
            _event: ChartEvent,
            _legendItem: LegendItem,
            legend: LegendElement<'doughnut'>
          ) => {
            const chart = legend.chart;

            chart.setActiveElements([]);
            chart.tooltip?.setActiveElements([], { x: 0, y: 0 });
            chart.update();
          },
        },
        tooltip: {
          callbacks: {
            label: (context: TooltipItem<'doughnut'>) => {
              const total = items.reduce((acc, item) => acc + item.value, 0);

              return ` ${context.parsed.toLocaleString()} (${formatShare(
                context.parsed,
                total
              )})`;
            },
          },
        },
      },
    };
  }, [colors, items]);

  return (
    <ChartContainer title={title}>
      <Doughnut data={data} options={options} />
    </ChartContainer>
  );
}

export default DoughnutChart;
