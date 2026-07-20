/** Default Studio skill-suggest debounce (ms). */
export const SKILL_SUGGEST_DEBOUNCE_MS = 600;

/** Minimum trimmed prompt length before the backend suggest endpoint is hit. */
export const SKILL_SUGGEST_MIN_CHARS = 12;

export function shouldRequestSkillSuggestions(prompt: string): boolean {
  return prompt.trim().length >= SKILL_SUGGEST_MIN_CHARS;
}

/**
 * Lightweight debounce used by Studio's prompt input.
 * Schedule replaces any pending call; only the latest value fires after delayMs.
 */
export class DebouncedAction {
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor(
    private readonly delayMs: number,
    private readonly action: (value: string) => void
  ) {}

  schedule(value: string): void {
    this.cancel();
    this.timer = setTimeout(() => {
      this.timer = undefined;
      this.action(value);
    }, this.delayMs);
  }

  cancel(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }
  }

  get isPending(): boolean {
    return this.timer !== undefined;
  }
}
