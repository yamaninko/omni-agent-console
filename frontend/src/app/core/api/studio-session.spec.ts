import { installLocalStorageMock } from '../../../test-localstorage';
import { getStudioSessionId } from './studio-session';

describe('getStudioSessionId', () => {
  beforeEach(() => {
    installLocalStorageMock();
  });

  it('creates a path-safe session id and reuses it', () => {
    const first = getStudioSessionId();
    const second = getStudioSessionId();
    expect(first).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
    expect(second).toBe(first);
    expect(localStorage.getItem('studio_session_id')).toBe(first);
  });

  it('regenerates when the stored value is invalid', () => {
    localStorage.setItem('studio_session_id', 'bad id with spaces!!');
    const id = getStudioSessionId();
    expect(id).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
    expect(id).not.toContain(' ');
  });
});
