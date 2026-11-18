import React, { useMemo } from 'react';
import StatisticsData from 'typings/Statistics';
import formatBytes from 'Utilities/Number/formatBytes';
import roundNumber from 'Utilities/Number/roundNumber';
import translate from 'Utilities/String/translate';
import BarChart, { BarChartItem } from './Charts/BarChart';
import DoughnutChart, { DoughnutChartItem } from './Charts/DoughnutChart';
import useChartColors from './Charts/useChartColors';
import StatisticsSummary, { SummaryItem } from './StatisticsSummary';
import styles from './StatisticsContent.css';

function formatCount(count: number) {
  return count.toLocaleString();
}

function filesAndSizeLines(trackFileCount: number, sizeOnDisk: number) {
  return [
    `${translate('TrackFiles')}: ${formatCount(trackFileCount)}`,
    `${translate('SizeOnDisk')}: ${formatBytes(sizeOnDisk)}`,
  ];
}

interface StatisticsContentProps {
  data: StatisticsData;
}

function StatisticsContent({ data }: StatisticsContentProps) {
  const colors = useChartColors();

  const summaryItems = useMemo<SummaryItem[]>(() => {
    const downloadedPercent = data.totalTrackCount
      ? roundNumber((data.downloadedTrackCount / data.totalTrackCount) * 100)
      : 0;

    const wantedAlbumCount = data.completedAlbumCount + data.missingAlbumCount;
    const completedAlbumPercent = wantedAlbumCount
      ? roundNumber((data.completedAlbumCount / wantedAlbumCount) * 100)
      : 0;

    const averageSizePerTrackFile = data.trackFileCount
      ? data.sizeOnDisk / data.trackFileCount
      : 0;

    return [
      {
        label: translate('Artists'),
        value: formatCount(data.artistCount),
        secondary: `${formatCount(data.monitoredArtistCount)} ${translate(
          'Monitored'
        )} · ${formatCount(data.completedArtistCount)} ${translate(
          'Completed'
        )}`,
      },
      {
        label: translate('Albums'),
        value: formatCount(data.albumCount),
        secondary: `${formatCount(data.monitoredAlbumCount)} ${translate(
          'Monitored'
        )} · ${formatCount(data.completedAlbumCount)} ${translate(
          'Completed'
        )}`,
      },
      {
        label: translate('Tracks'),
        value: formatCount(data.totalTrackCount),
        secondary: `${formatCount(data.monitoredTrackCount)} ${translate(
          'Monitored'
        )}`,
      },
      {
        label: translate('TrackFiles'),
        value: formatCount(data.trackFileCount),
        secondary: `${downloadedPercent}% ${translate('Downloaded')}`,
      },
      {
        label: translate('MissingAlbums'),
        value: formatCount(data.missingAlbumCount),
        secondary: `${completedAlbumPercent}% ${translate(
          'Completed'
        )} · ${formatCount(data.missingTrackCount)} ${translate(
          'MissingTracks'
        )}`,
      },
      {
        label: translate('SizeOnDisk'),
        value: formatBytes(data.sizeOnDisk),
        secondary: `${formatBytes(averageSizePerTrackFile)} ${translate(
          'AverageSizePerTrackFile'
        )}`,
      },
    ];
  }, [data]);

  const statusItems = useMemo<DoughnutChartItem[]>(() => {
    // Continuing and Inactive use the artist index footer's legend colours
    return [
      {
        label: translate('Continuing'),
        value: data.continuingArtistCount,
        color: colors.primaryColor,
      },
      {
        label: translate('Inactive'),
        value: data.endedArtistCount,
        color: colors.successColor,
      },
      {
        label: translate('Deleted'),
        value: data.deletedArtistCount,
        color: colors.dangerColor,
      },
    ];
  }, [colors, data]);

  const albumTypeItems = useMemo<DoughnutChartItem[]>(() => {
    return data.albumTypeStatistics.map((albumType, index) => {
      return {
        label: albumType.albumType,
        value: albumType.albumCount,
        color: colors.palette[index % colors.palette.length],
      };
    });
  }, [colors, data]);

  const trackItems = useMemo<DoughnutChartItem[]>(() => {
    // Tracks with no file that are released but not wanted: the artist or album is unmonitored
    const unmonitoredCount = Math.max(
      0,
      data.totalTrackCount -
        data.downloadedTrackCount -
        data.missingTrackCount -
        data.unreleasedTrackCount
    );

    return [
      {
        label: translate('Downloaded'),
        value: data.downloadedTrackCount,
        color: colors.successColor,
      },
      {
        label: translate('Missing'),
        value: data.missingTrackCount,
        color: colors.dangerColor,
      },
      {
        label: translate('Unreleased'),
        value: data.unreleasedTrackCount,
        color: colors.primaryColor,
      },
      {
        label: translate('Unmonitored'),
        value: unmonitoredCount,
        color: colors.grayColor,
      },
    ];
  }, [colors, data]);

  const qualityProfileItems = useMemo<BarChartItem[]>(() => {
    return data.qualityProfileStatistics.map((profile) => {
      return {
        label: profile.name,
        value: profile.artistCount,
        tooltipLines: filesAndSizeLines(
          profile.trackFileCount,
          profile.sizeOnDisk
        ),
      };
    });
  }, [data]);

  const metadataProfileItems = useMemo<BarChartItem[]>(() => {
    return data.metadataProfileStatistics.map((profile) => {
      return {
        label: profile.name,
        value: profile.artistCount,
      };
    });
  }, [data]);

  const qualityItems = useMemo<BarChartItem[]>(() => {
    return data.qualityStatistics.map((quality) => {
      return {
        label: quality.quality.name,
        value: quality.trackFileCount,
        tooltipLines: [
          `${translate('SizeOnDisk')}: ${formatBytes(quality.sizeOnDisk)}`,
        ],
      };
    });
  }, [data]);

  const tagItems = useMemo<BarChartItem[]>(() => {
    return data.tagStatistics.map((tag) => {
      return {
        label: tag.label,
        value: tag.artistCount,
        tooltipLines: filesAndSizeLines(tag.trackFileCount, tag.sizeOnDisk),
      };
    });
  }, [data]);

  const decadeItems = useMemo<BarChartItem[]>(() => {
    return data.decadeStatistics.map((decade) => {
      return {
        label:
          decade.decade == null
            ? translate('Unknown')
            : translate('Decade', { decade: decade.decade }),
        value: decade.albumCount,
        tooltipLines: filesAndSizeLines(
          decade.trackFileCount,
          decade.sizeOnDisk
        ),
      };
    });
  }, [data]);

  const secondaryTypeItems = useMemo<BarChartItem[]>(() => {
    return data.secondaryTypeStatistics.map((secondaryType) => {
      return {
        label: secondaryType.secondaryType,
        value: secondaryType.albumCount,
      };
    });
  }, [data]);

  return (
    <>
      <StatisticsSummary items={summaryItems} />

      <div className={styles.charts}>
        <DoughnutChart title={translate('ArtistStatus')} items={statusItems} />

        <DoughnutChart
          title={translate('PrimaryAlbumTypes')}
          items={albumTypeItems}
        />

        <DoughnutChart title={translate('Tracks')} items={trackItems} />
      </div>

      <div className={styles.charts}>
        <BarChart
          title={translate('QualityProfiles')}
          items={qualityProfileItems}
        />

        <BarChart
          title={translate('MetadataProfiles')}
          items={metadataProfileItems}
        />

        <BarChart title={translate('TrackQualities')} items={qualityItems} />
      </div>

      <div className={styles.charts}>
        <BarChart title={translate('AlbumsByDecade')} items={decadeItems} />

        <BarChart
          title={translate('SecondaryAlbumTypes')}
          items={secondaryTypeItems}
        />

        {tagItems.length ? (
          <BarChart title={translate('Tags')} items={tagItems} />
        ) : null}
      </div>
    </>
  );
}

export default StatisticsContent;
