import { describe, expect, it } from 'vitest';
import { parseEvents, type AssistantEvent } from './api';

describe('parseEvents', () => {
  it('reads whole events and keeps a partial one for the next chunk', () => {
    const events: AssistantEvent[] = [];
    const rest = parseEvents('data: {"type":"delta","text":"Hel"}\n\ndata: {"type":"delta","text":"lo"}\n\ndata: {"type":"do', (event) => events.push(event));

    expect(events.map((event) => event.text)).toEqual(['Hel', 'lo']);
    expect(rest).toBe('data: {"type":"do');

    parseEvents(`${rest}ne"}\n\n`, (event) => events.push(event));
    expect(events.at(-1)?.type).toBe('done');
  });

  it('joins multi-line data and ignores comments and empty chunks', () => {
    const events: AssistantEvent[] = [];
    parseEvents(': keep-alive\n\ndata: {"type":\ndata: "error","code":"SprintBoot:AssistantBusy"}\n\n\n\n', (event) => events.push(event));

    expect(events).toEqual([{ type: 'error', code: 'SprintBoot:AssistantBusy' }]);
  });
});
