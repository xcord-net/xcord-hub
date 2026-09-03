import { describe, it, expect, vi } from 'vitest';
import {
  parseHubMessage,
  postHubMessage,
  HubMessageType,
  HUB_PROTOCOL_VERSION,
  originOf,
} from './hubProtocol';

const TRUSTED = 'https://inst.test';
const trustAll = { isTrustedOrigin: () => true };
const trustOne = { isTrustedOrigin: (o: string) => o === TRUSTED };

function event(data: unknown, origin = TRUSTED, source?: unknown): MessageEvent {
  return { data, origin, source } as unknown as MessageEvent;
}

describe('originOf', () => {
  it('reduces a url to its origin', () => {
    expect(originOf('https://inst.test/some/path?q=1')).toBe('https://inst.test');
  });

  it('returns null for something that is not a url', () => {
    expect(originOf('not a url')).toBeNull();
  });
});

describe('parseHubMessage trust', () => {
  it('drops anything from an untrusted origin', () => {
    const msg = { type: HubMessageType.Unread, instanceUrl: 'https://evil.test', count: 3 };
    expect(parseHubMessage(event(msg, 'https://evil.test'), trustOne)).toBeNull();
  });

  it('drops a message from the wrong frame when a source is expected', () => {
    const frame = {} as MessageEventSource;
    const other = {} as MessageEventSource;
    const msg = { type: HubMessageType.HubKey, hubKey: 'abc' };
    expect(parseHubMessage(event(msg, TRUSTED, other), { ...trustAll, expectedSource: frame })).toBeNull();
  });

  it('accepts a message from the expected frame', () => {
    const frame = {} as MessageEventSource;
    const msg = { type: HubMessageType.HubKey, hubKey: 'abc' };
    expect(parseHubMessage(event(msg, TRUSTED, frame), { ...trustAll, expectedSource: frame }))
      .toEqual({ type: HubMessageType.HubKey, hubKey: 'abc' });
  });

  it('ignores an unknown message type', () => {
    expect(parseHubMessage(event({ type: 'xcord_something_else' }), trustAll)).toBeNull();
  });

  it('ignores a payload that is not an object', () => {
    expect(parseHubMessage(event('xcord_unread'), trustAll)).toBeNull();
    expect(parseHubMessage(event(null), trustAll)).toBeNull();
  });

  // An older instance predates the version field; a wrong version is a real
  // mismatch and is refused.
  it('accepts a message with no version', () => {
    expect(parseHubMessage(event({ type: HubMessageType.LeaveConversation }), trustAll)).not.toBeNull();
  });

  it('refuses a message from a different protocol version', () => {
    const msg = { type: HubMessageType.LeaveConversation, version: HUB_PROTOCOL_VERSION + 1 };
    expect(parseHubMessage(event(msg), trustAll)).toBeNull();
  });
});

describe('parseHubMessage unread', () => {
  const unread = (over: Record<string, unknown> = {}) => ({
    type: HubMessageType.Unread,
    instanceUrl: `${TRUSTED}/app`,
    count: 4,
    ...over,
  });

  it('parses a well-formed count', () => {
    expect(parseHubMessage(event(unread()), trustOne)).toEqual({
      type: HubMessageType.Unread,
      instanceUrl: `${TRUSTED}/app`,
      count: 4,
    });
  });

  // A trusted instance must not be able to report counts for a different one.
  it('refuses a count claimed for another origin', () => {
    const msg = unread({ instanceUrl: 'https://other.test' });
    expect(parseHubMessage(event(msg), trustOne)).toBeNull();
  });

  it('refuses a negative or non-finite count', () => {
    expect(parseHubMessage(event(unread({ count: -1 })), trustOne)).toBeNull();
    expect(parseHubMessage(event(unread({ count: Number.NaN })), trustOne)).toBeNull();
    expect(parseHubMessage(event(unread({ count: Infinity })), trustOne)).toBeNull();
  });

  it('refuses a count that is not a number', () => {
    expect(parseHubMessage(event(unread({ count: '4' })), trustOne)).toBeNull();
  });

  it('accepts zero, which is how an instance clears its badge', () => {
    expect(parseHubMessage(event(unread({ count: 0 })), trustOne)).not.toBeNull();
  });
});

describe('parseHubMessage hub key', () => {
  it('accepts a well-formed key', () => {
    const msg = { type: HubMessageType.HubKey, hubKey: 'aZ0_-key' };
    expect(parseHubMessage(event(msg), trustAll)).toEqual({
      type: HubMessageType.HubKey,
      hubKey: 'aZ0_-key',
    });
  });

  it('refuses a key with characters outside the pattern', () => {
    const msg = { type: HubMessageType.HubKey, hubKey: 'has spaces' };
    expect(parseHubMessage(event(msg), trustAll)).toBeNull();
  });

  it('refuses an empty or over-long key', () => {
    expect(parseHubMessage(event({ type: HubMessageType.HubKey, hubKey: '' }), trustAll)).toBeNull();
    expect(parseHubMessage(event({ type: HubMessageType.HubKey, hubKey: 'a'.repeat(65) }), trustAll)).toBeNull();
  });
});

describe('postHubMessage', () => {
  it('stamps the version and targets an explicit origin', () => {
    const target = { postMessage: vi.fn() } as unknown as Window;
    postHubMessage(target, TRUSTED, { type: HubMessageType.LeaveConversation });

    expect(target.postMessage).toHaveBeenCalledWith(
      { type: HubMessageType.LeaveConversation, version: HUB_PROTOCOL_VERSION },
      TRUSTED,
    );
  });

  // Sending to '*' would hand the payload to whatever is loaded there instead.
  it('never broadcasts to a wildcard origin', () => {
    const target = { postMessage: vi.fn() } as unknown as Window;
    postHubMessage(target, TRUSTED, { type: HubMessageType.LeaveConversation });
    expect((target.postMessage as ReturnType<typeof vi.fn>).mock.calls[0][1]).not.toBe('*');
  });

  it('does nothing when the target window is gone', () => {
    expect(() => postHubMessage(null, TRUSTED, { type: HubMessageType.LeaveConversation }))
      .not.toThrow();
  });
});
