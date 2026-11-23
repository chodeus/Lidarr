type Measure = (text: string) => number;

function cutLine(text: string, maxWidth: number, measure: Measure) {
  if (measure(text) <= maxWidth) {
    return text;
  }

  let end = text.length - 1;

  while (end > 0 && measure(`${text.slice(0, end).trimEnd()}…`) > maxWidth) {
    end--;
  }

  return `${text.slice(0, end).trimEnd()}…`;
}

// The longest start of text that fits, ended at a space when one falls inside it
function lineEnd(text: string, maxWidth: number, measure: Measure) {
  let end = text.length;

  while (end > 1 && measure(text.slice(0, end)) > maxWidth) {
    end--;
  }

  const space = text.lastIndexOf(' ', end);

  return space > 0 ? space : end;
}

export default function fitLabel(
  label: string,
  maxWidth: number,
  measure: Measure,
  maxLines = 1
) {
  const lines: string[] = [];
  let rest = label;

  while (lines.length < maxLines - 1 && measure(rest) > maxWidth) {
    const end = lineEnd(rest, maxWidth, measure);

    lines.push(rest.slice(0, end));
    rest = rest.slice(end).trimStart();
  }

  lines.push(cutLine(rest, maxWidth, measure));

  return lines;
}
