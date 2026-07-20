const SESSION_KEY = 'studio_session_id';
const SESSION_ID_PATTERN = /^[A-Za-z0-9_-]{8,64}$/;

/**
 * Browser-local session identity for the shared-lab deployment profile.
 * Generated once and reused; the backend ignores it in the default
 * (laptop-only) profile, and scopes tasks/workspace to it when
 * SHARED_LAB=true. Charset mirrors the backend's SharedLabPolicy
 * (path-safe: the id becomes a workspace folder name).
 */
export function getStudioSessionId(): string {
  let sessionId = localStorage.getItem(SESSION_KEY);
  if (!sessionId || !SESSION_ID_PATTERN.test(sessionId)) {
    sessionId = crypto.randomUUID();
    localStorage.setItem(SESSION_KEY, sessionId);
  }
  return sessionId;
}
