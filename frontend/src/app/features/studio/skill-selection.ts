/**
 * Pure skill-selection helpers used by Studio.
 * Extracted so the merge / dismiss rules can be unit-tested without TestBed.
 */

export function mergeSelectedSkillIds(
  manual: readonly string[],
  auto: readonly string[],
  dismissed: readonly string[]
): string[] {
  const dismissedSet = new Set(dismissed);
  const autoOnly = auto.filter((id) => !dismissedSet.has(id) && !manual.includes(id));
  return [...manual, ...autoOnly];
}

export function isAutoSuggestedSkill(
  id: string,
  manual: readonly string[],
  auto: readonly string[],
  dismissed: readonly string[]
): boolean {
  return auto.includes(id) && !manual.includes(id) && !dismissed.includes(id);
}

/**
 * Chip click: manual toggle, or dismiss an auto suggestion, or promote a
 * previously dismissed id into a manual pick.
 */
export function applySkillToggle(
  id: string,
  manual: readonly string[],
  auto: readonly string[],
  dismissed: readonly string[]
): { manual: string[]; dismissed: string[] } {
  if (manual.includes(id)) {
    return {
      manual: manual.filter((x) => x !== id),
      dismissed: [...dismissed]
    };
  }

  if (isAutoSuggestedSkill(id, manual, auto, dismissed)) {
    return {
      manual: [...manual],
      dismissed: dismissed.includes(id) ? [...dismissed] : [...dismissed, id]
    };
  }

  return {
    manual: [...manual, id],
    dismissed: dismissed.filter((x) => x !== id)
  };
}
