import { PageLoading } from "../../../src/ui";

/** Shows at once on navigation, while the server reads the devices and staff names. */
export default function Loading() {
  return <PageLoading title="Scanner devices" label="Loading scanner devices…" />;
}
