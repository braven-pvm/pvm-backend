import styles from "./column-chart.module.css";

export type ChartSeries = {
  /** "FY2027". */
  name: string;
  values: number[];
};

type ColumnChartProps = {
  /** What the chart shows, for the caption and screen readers: "Sales by financial month". */
  label: string;
  /** One label for each group of columns: "Mar", "Apr". 12 at most. */
  categories: string[];
  /** 1 or 2 series. The first one is the main series, such as this year. The second one is the comparison. */
  series: ChartSeries[];
  /** Formats a value for the axis and the table: "R 1.2 m". */
  format: (value: number) => string;
};

/**
 * Columns by period, with an optional comparison series, such as this year
 * against last year. No chart library. A table under the chart gives every
 * value to screen readers.
 * Spec: docs/design/design-system.md#columnchart
 */
export function ColumnChart({ label, categories, series, format }: ColumnChartProps) {
  const max = Math.max(1, ...series.flatMap((item) => item.values));
  const height = (value: number) => `${Math.max(0, Math.min(100, (value / max) * 100))}%`;

  return (
    <figure className={styles.figure}>
      <figcaption className={styles.legend}>
        <span className="sr-only">{label}: </span>
        {series.map((item, index) => (
          <span key={item.name} className={styles.key}>
            <span className={styles.swatch} data-series={index} aria-hidden="true" />
            {item.name}
          </span>
        ))}
      </figcaption>
      <div className={styles.plot} aria-hidden="true">
        <div className={styles.axis}>
          <span>{format(max)}</span>
          <span>{format(max / 2)}</span>
          <span>{format(0)}</span>
        </div>
        <div className={styles.columns}>
          {categories.map((category, column) => (
            <div key={category} className={styles.group}>
              <div className={styles.bars}>
                {series.map((item, index) => (
                  <span
                    key={item.name}
                    className={styles.bar}
                    data-series={index}
                    style={{ height: height(item.values[column] ?? 0) }}
                    title={`${item.name} ${category}: ${format(item.values[column] ?? 0)}`}
                  />
                ))}
              </div>
              <span className={styles.category}>{category}</span>
            </div>
          ))}
        </div>
      </div>
      <table className="sr-only">
        <caption>{label}</caption>
        <thead>
          <tr>
            <th scope="col">Month</th>
            {series.map((item) => (
              <th key={item.name} scope="col">
                {item.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {categories.map((category, column) => (
            <tr key={category}>
              <th scope="row">{category}</th>
              {series.map((item) => (
                <td key={item.name}>{format(item.values[column] ?? 0)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}
