import assert from "node:assert/strict";
import { test } from "node:test";
import nextConfig, { serverActionBodySizeLimit } from "../next.config.ts";

test("server actions accept a sell-out file of up to 25 MB", () => {
  assert.equal(nextConfig.experimental?.serverActions?.bodySizeLimit, serverActionBodySizeLimit);
  const megabytes = Number.parseInt(serverActionBodySizeLimit, 10);
  assert.ok(megabytes >= 25, `${serverActionBodySizeLimit} is below the 25 MB that the API accepts`);
});
