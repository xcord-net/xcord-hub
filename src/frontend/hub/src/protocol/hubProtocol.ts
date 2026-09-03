/**
 * The hub <-> instance postMessage protocol.
 *
 * The hub embeds each instance in an iframe, and each instance embeds the hub's
 * header in one of its own, so messages cross an origin boundary in both
 * directions. Before this module the three message types were string literals
 * in three files, each with its own hand-rolled validation - which meant three
 * places to get an origin check subtly wrong.
 *
 * Every message is parsed here, and a message that fails any check is dropped
 * rather than partially trusted. The rules, in order:
 *
 *  1. The sending origin must be one the receiver already trusts. Federation
 *     rules out a static allowlist, so the caller supplies the predicate.
 *  2. The envelope must carry a known type and this protocol version.
 *  3. Every field must be the right shape, and any origin the payload claims
 *     must match the origin it actually arrived from - a trusted instance does
 *     not get to report counts on behalf of another one.
 *
 * This file is duplicated verbatim in xcord-fed. The two apps are separate
 * packages either side of a submodule boundary, so a shared copy is the cost of
 * not introducing a third package to hold ten types.
 */

export const HUB_PROTOCOL_VERSION = 1;

export const HubMessageType = {
  /** instance -> hub: this instance's total unread count changed. */
  Unread: 'xcord_unread',
  /** hub -> instance: you are no longer the visible tab; drop the room. */
  LeaveConversation: 'xcord_leave_conversation',
  /** hub header -> instance: the key that identifies this user to the hub. */
  HubKey: 'xcord_hub_key',
} as const;

export type HubMessageType = (typeof HubMessageType)[keyof typeof HubMessageType];

export interface UnreadMessage {
  type: typeof HubMessageType.Unread;
  /** Which instance the count belongs to. Must match the sending origin. */
  instanceUrl: string;
  count: number;
}

export interface LeaveConversationMessage {
  type: typeof HubMessageType.LeaveConversation;
}

export interface HubKeyMessage {
  type: typeof HubMessageType.HubKey;
  hubKey: string;
}

export type HubMessage = UnreadMessage | LeaveConversationMessage | HubKeyMessage;

/** Hub keys are opaque to both sides, so the only check is the shape. */
export const HUB_KEY_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export function originOf(url: string): string | null {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

export interface ParseOptions {
  /** True when the receiver already trusts this origin. */
  isTrustedOrigin: (origin: string) => boolean;
  /**
   * When set, the message must also have come from this exact window. Used
   * where the receiver knows which frame it is talking to, so a third frame on
   * a trusted origin cannot speak for it.
   */
  expectedSource?: MessageEventSource | null;
}

/**
 * Validate an incoming message. Returns the typed message, or null if anything
 * about it is untrusted or malformed.
 */
export function parseHubMessage(event: MessageEvent, opts: ParseOptions): HubMessage | null {
  if (!opts.isTrustedOrigin(event.origin)) return null;
  if (opts.expectedSource !== undefined && event.source !== opts.expectedSource) return null;

  const data: unknown = event.data;
  if (typeof data !== 'object' || data === null) return null;

  const envelope = data as { type?: unknown; version?: unknown };

  // Version is optional on the wire so an instance running an older build still
  // talks to a newer hub. An explicit mismatch is rejected; absence is not.
  if (envelope.version !== undefined && envelope.version !== HUB_PROTOCOL_VERSION) return null;

  switch (envelope.type) {
    case HubMessageType.Unread: {
      const { instanceUrl, count } = data as { instanceUrl?: unknown; count?: unknown };
      if (typeof instanceUrl !== 'string') return null;
      if (typeof count !== 'number' || !Number.isFinite(count) || count < 0) return null;
      // The payload cannot claim to speak for an origin it did not come from.
      if (originOf(instanceUrl) !== event.origin) return null;
      return { type: HubMessageType.Unread, instanceUrl, count };
    }

    case HubMessageType.LeaveConversation:
      return { type: HubMessageType.LeaveConversation };

    case HubMessageType.HubKey: {
      const { hubKey } = data as { hubKey?: unknown };
      if (typeof hubKey !== 'string' || !HUB_KEY_PATTERN.test(hubKey)) return null;
      return { type: HubMessageType.HubKey, hubKey };
    }

    default:
      return null;
  }
}

/**
 * Send a message across the boundary. Always targets an explicit origin - never
 * `'*'`, which would hand the payload to whatever happens to be loaded there.
 */
export function postHubMessage(
  target: Window | null | undefined,
  targetOrigin: string,
  message: HubMessage,
): void {
  if (!target) return;
  target.postMessage({ ...message, version: HUB_PROTOCOL_VERSION }, targetOrigin);
}
