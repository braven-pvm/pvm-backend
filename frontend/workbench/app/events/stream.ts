import type { AcumaticaPushNotificationEventView, IntegrationMessageView } from "../../src/api/client";

export type EventSource = "webhook" | "delivery" | "outbox";

/** One row of the Events stream, whatever its source. */
export type StreamEvent = {
  id: string;
  source: EventSource;
  at: string;
  name: string;
  detail: string;
  status: string;
};

const sourceLabels: Record<EventSource, string> = {
  webhook: "Acumatica webhook",
  delivery: "Worker delivery",
  outbox: "Queued command",
};

export const sourceLabel = (source: EventSource) => sourceLabels[source];

/** Webhooks, worker deliveries and queued commands in one list, newest first. */
export function buildStream(webhooks: AcumaticaPushNotificationEventView, messages: IntegrationMessageView): StreamEvent[] {
  const events: StreamEvent[] = [
    ...webhooks.events.map((event) => ({
      id: `webhook-${event.id}`,
      source: "webhook" as const,
      at: event.receivedAt,
      name: event.queryName,
      detail: `Transaction ${event.transactionId} · ${event.insertedCount} added · ${event.deletedCount} removed`,
      status: event.duplicateCount > 0 ? "Duplicate" : "Processed",
    })),
    ...messages.deliveries.map((delivery) => ({
      id: `delivery-${delivery.id}`,
      source: "delivery" as const,
      at: delivery.updatedAt,
      name: delivery.messageType,
      detail: [delivery.queueName, delivery.errorSummary ?? delivery.deadLetterReason, `${delivery.deliveryCount} ${delivery.deliveryCount === 1 ? "try" : "tries"}`]
        .filter(Boolean)
        .join(" · "),
      status: delivery.status,
    })),
    ...messages.outbox.map((message) => ({
      id: `outbox-${message.id}`,
      source: "outbox" as const,
      at: message.updatedAt,
      name: message.messageType,
      detail: [message.queueName, message.lastErrorSummary].filter(Boolean).join(" · "),
      status: message.status,
    })),
  ];
  return events.sort((a, b) => b.at.localeCompare(a.at));
}
