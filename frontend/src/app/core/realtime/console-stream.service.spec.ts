import { TestBed } from '@angular/core/testing';
import { ConsoleEvent } from '../models';
import { ConsoleStreamService } from './console-stream.service';

function sampleEvent(id: string, eventType = 'AgentStep'): ConsoleEvent {
  return {
    id,
    taskRunId: 'task-1',
    eventType,
    message: `msg-${id}`,
    createdAt: new Date().toISOString()
  };
}

describe('ConsoleStreamService (local event buffer)', () => {
  let service: ConsoleStreamService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ConsoleStreamService);
  });

  it('setEvents replaces the buffer', () => {
    service.setEvents([sampleEvent('a'), sampleEvent('b')]);
    expect(service.events().map((e) => e.id)).toEqual(['a', 'b']);
  });

  it('reset clears the buffer', () => {
    service.setEvents([sampleEvent('a')]);
    service.reset();
    expect(service.events()).toEqual([]);
  });
});
