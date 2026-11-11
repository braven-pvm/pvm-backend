import { Stack, Tile, TileGrid, Notice } from "../../../src/ui";
import { PageFrame, PageHeader } from "../../../src/ui";
import { devOnly, templateLinks } from "./sample";

export const metadata = { title: "Page templates" };

export default function TemplatesIndex() {
  devOnly();
  return (
    <PageFrame>
      <PageHeader
        eyebrow="Design system"
        title="Page templates"
        description="Every page is one of these five. Each example uses only components from src/ui and example data."
      />
      <TileGrid min={240}>
        {templateLinks.map((link) => (
          <Tile key={link.href} title={link.title} meta={link.use} href={link.href} />
        ))}
      </TileGrid>
      <Stack gap="s">
        <Notice tone="info" title="States to try">
          <p>
            List: add ?state=loading or ?state=error to the URL, choose Needs review then Sent for the empty state, and use
            the filter bar example. Dashboard: add ?clear=1 for the all-clear line. Task: go through the three steps.
          </p>
        </Notice>
      </Stack>
    </PageFrame>
  );
}
