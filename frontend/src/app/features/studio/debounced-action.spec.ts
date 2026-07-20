import {
  DebouncedAction,
  SKILL_SUGGEST_DEBOUNCE_MS,
  SKILL_SUGGEST_MIN_CHARS,
  shouldRequestSkillSuggestions
} from './debounced-action';

describe('shouldRequestSkillSuggestions', () => {
  it('requires a trimmed length of at least SKILL_SUGGEST_MIN_CHARS', () => {
    expect(SKILL_SUGGEST_MIN_CHARS).toBe(12);
    expect(shouldRequestSkillSuggestions('short')).toBe(false);
    // 12 chars after trim
    expect(shouldRequestSkillSuggestions('   123456789012')).toBe(true);
    expect(shouldRequestSkillSuggestions('  too short  ')).toBe(false);
  });
});

describe('DebouncedAction', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('does not fire before the debounce delay', () => {
    const action = vi.fn();
    const debounced = new DebouncedAction(SKILL_SUGGEST_DEBOUNCE_MS, action);

    debounced.schedule('go rest api with redis');
    vi.advanceTimersByTime(SKILL_SUGGEST_DEBOUNCE_MS - 1);

    expect(action).not.toHaveBeenCalled();
    expect(debounced.isPending).toBe(true);
  });

  it('fires once after the delay with the scheduled value', () => {
    const action = vi.fn();
    const debounced = new DebouncedAction(SKILL_SUGGEST_DEBOUNCE_MS, action);

    debounced.schedule('first prompt long enough');
    vi.advanceTimersByTime(SKILL_SUGGEST_DEBOUNCE_MS);

    expect(action).toHaveBeenCalledTimes(1);
    expect(action).toHaveBeenCalledWith('first prompt long enough');
    expect(debounced.isPending).toBe(false);
  });

  it('collapses rapid schedules to only the latest value', () => {
    const action = vi.fn();
    const debounced = new DebouncedAction(SKILL_SUGGEST_DEBOUNCE_MS, action);

    debounced.schedule('first value that is long');
    vi.advanceTimersByTime(200);
    debounced.schedule('second value that is long');
    vi.advanceTimersByTime(200);
    debounced.schedule('third and final value xx');
    vi.advanceTimersByTime(SKILL_SUGGEST_DEBOUNCE_MS);

    expect(action).toHaveBeenCalledTimes(1);
    expect(action).toHaveBeenCalledWith('third and final value xx');
  });

  it('cancel aborts a pending call', () => {
    const action = vi.fn();
    const debounced = new DebouncedAction(SKILL_SUGGEST_DEBOUNCE_MS, action);

    debounced.schedule('will be cancelled prompt');
    debounced.cancel();
    vi.advanceTimersByTime(SKILL_SUGGEST_DEBOUNCE_MS);

    expect(action).not.toHaveBeenCalled();
  });
});
