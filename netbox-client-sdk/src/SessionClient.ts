import { NetBoxHttp } from "./http.ts";
import type { Session } from "./types.ts";

/**
 * Session control-plane client - thin wrapper over netbox-api's
 * `/sessions` routes. Matches the exact method surface requested for
 * Phase 10: listSessions/createSession/startSession/stopSession/
 * joinSession/leaveSession. "join"/"leave" here mean joining/leaving the
 * *session itself* as a viewer/spectator concept the dashboard can call
 * before a controller slot is picked - the more specific "join a
 * controller slot" flow lives on PlayerClient.
 */
export class SessionClient {
  private readonly http: NetBoxHttp;

  public constructor(http: NetBoxHttp) {
    this.http = http;
  }

  public async listSessions(): Promise<Session[]> {
    const sessions = await this.http.request<Session[]>("GET", "/sessions");
    return sessions ?? [];
  }

  public async createSession(): Promise<Session> {
    const session = await this.http.request<Session>("POST", "/sessions");
    if (!session) {
      throw new Error("createSession: empty response from API gateway");
    }
    return session;
  }

  public async getSession(id: number): Promise<Session | null> {
    return this.http.request<Session>("GET", `/sessions/${id}`);
  }

  public async startSession(id: number): Promise<Session> {
    const session = await this.http.request<Session>("POST", `/sessions/${id}/start`);
    if (!session) {
      throw new Error("startSession: empty response from API gateway");
    }
    return session;
  }

  public async stopSession(id: number): Promise<Session> {
    const session = await this.http.request<Session>("POST", `/sessions/${id}/stop`);
    if (!session) {
      throw new Error("stopSession: empty response from API gateway");
    }
    return session;
  }

  public async destroySession(id: number): Promise<void> {
    await this.http.request<void>("DELETE", `/sessions/${id}`);
  }

  /** Convenience alias over startSession() for dashboards that model
   * "joining" as simply bringing an already-created session to Running. */
  public async joinSession(id: number): Promise<Session> {
    return this.startSession(id);
  }

  /** Convenience alias over stopSession() - see joinSession() note above. */
  public async leaveSession(id: number): Promise<Session> {
    return this.stopSession(id);
  }
}
