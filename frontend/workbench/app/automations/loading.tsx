import { PageLoading } from "../../src/ui";

/** Shows at once on navigation, while the server reads the data for this page. */
export default function Loading() {
  return <PageLoading title="Automations" label="Loading automations…" />;
}
