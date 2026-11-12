/**
 * Starts a data call before the user check finishes, so the two run at the
 * same time. The promise is marked as handled, so a page that redirects to
 * sign-in first does not leave an unhandled rejection. Await the promise as
 * usual: it still rejects there.
 */
export function startEarly<T>(promise: Promise<T>): Promise<T> {
  promise.catch(() => undefined);
  return promise;
}
