import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ConsoleEvent } from '../models';
import { getStudioSessionId } from '../api/studio-session';

const HUB_URL = '/ws/consoleHub';

@Injectable({ providedIn: 'root' })
export class ConsoleStreamService {
  readonly events = signal<ConsoleEvent[]>([]);
  private connection?: HubConnection;
  private subscribedTaskId?: string;

  async connect(taskId: string): Promise<void> {
    if (!this.connection) {
      const apiKey = localStorage.getItem('console_api_key');
      // WebSocket upgrades cannot carry custom headers, so the shared-lab
      // session id rides in the query string (the backend reads both).
      const hubUrl = `${HUB_URL}?session_id=${encodeURIComponent(getStudioSessionId())}`;
      this.connection = new HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: apiKey ? () => apiKey : undefined
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      this.connection.on('ReceiveConsoleEvent', (event: ConsoleEvent) => {
        this.events.update((events) => {
          if (events.some((current) => current.id === event.id)) {
            return events;
          }
          if (event.eventType === 'TaskStarted') {
            const filtered = events.filter(e => e.eventType !== 'TaskStarted');
            return [...filtered, event];
          }
          return [...events, event];
        });
      });

      await this.connection.start();
    }

    if (this.subscribedTaskId && this.subscribedTaskId !== taskId) {
      await this.connection.invoke('UnsubscribeTask', this.subscribedTaskId);
    }

    await this.connection.invoke('SubscribeTask', taskId);
    this.subscribedTaskId = taskId;
  }

  reset(): void {
    this.events.set([]);
  }

  setEvents(events: ConsoleEvent[]): void {
    this.events.set(events);
  }
}
