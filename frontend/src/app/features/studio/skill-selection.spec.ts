import { applySkillToggle, isAutoSuggestedSkill, mergeSelectedSkillIds } from './skill-selection';

describe('mergeSelectedSkillIds', () => {
  it('merges manual and auto without duplicates', () => {
    expect(mergeSelectedSkillIds(['a'], ['a', 'b'], [])).toEqual(['a', 'b']);
  });

  it('drops dismissed auto suggestions while keeping manual', () => {
    expect(mergeSelectedSkillIds(['a'], ['b', 'c'], ['b'])).toEqual(['a', 'c']);
  });

  it('returns only manual when auto is empty or fully dismissed', () => {
    expect(mergeSelectedSkillIds(['x', 'y'], [], [])).toEqual(['x', 'y']);
    expect(mergeSelectedSkillIds(['x'], ['x', 'y'], ['y'])).toEqual(['x']);
  });

  it('preserves manual order and appends remaining auto', () => {
    expect(mergeSelectedSkillIds(['m2', 'm1'], ['a1', 'm1'], [])).toEqual(['m2', 'm1', 'a1']);
  });
});

describe('isAutoSuggestedSkill', () => {
  it('is true only for non-manual, non-dismissed auto ids', () => {
    expect(isAutoSuggestedSkill('b', ['a'], ['b'], [])).toBe(true);
    expect(isAutoSuggestedSkill('a', ['a'], ['a', 'b'], [])).toBe(false);
    expect(isAutoSuggestedSkill('b', [], ['b'], ['b'])).toBe(false);
    expect(isAutoSuggestedSkill('z', [], ['b'], [])).toBe(false);
  });
});

describe('applySkillToggle', () => {
  it('removes a manual skill without touching dismissed', () => {
    expect(applySkillToggle('a', ['a', 'b'], ['c'], ['d'])).toEqual({
      manual: ['b'],
      dismissed: ['d']
    });
  });

  it('dismisses an auto-suggested chip instead of promoting it to manual', () => {
    expect(applySkillToggle('c', ['a'], ['c'], [])).toEqual({
      manual: ['a'],
      dismissed: ['c']
    });
  });

  it('promotes an unselected chip to manual and undismisses it', () => {
    expect(applySkillToggle('z', ['a'], [], ['z'])).toEqual({
      manual: ['a', 'z'],
      dismissed: []
    });
  });
});
