import {
  beginContinue,
  beginCreateOrRerun,
  onCancelAccepted,
  onCancelError,
  onContinueTaskError,
  onCreateTaskError,
  onRunTaskAccepted,
  onRunTaskError,
  onStatusPollError,
  onTaskTerminalStatus
} from './studio-run-state';

describe('studio run state transitions', () => {
  it('beginCreateOrRerun sets pending and clears running', () => {
    expect(beginCreateOrRerun()).toEqual({ pending: true, running: false });
  });

  it('beginContinue matches create/rerun lock', () => {
    expect(beginContinue()).toEqual({ pending: true, running: false });
  });

  it('create failure clears pending (no stuck spinner)', () => {
    expect(onCreateTaskError()).toEqual({ pending: false, running: false });
  });

  it('continue failure clears pending (no stuck spinner)', () => {
    expect(onContinueTaskError()).toEqual({ pending: false, running: false });
  });

  it('run accepted hands off to running state', () => {
    expect(onRunTaskAccepted()).toEqual({ pending: false, running: true });
  });

  it('run failure clears both flags (stuck-spinner lock)', () => {
    expect(onRunTaskError()).toEqual({ pending: false, running: false });
  });

  it('cancel success stops running', () => {
    expect(onCancelAccepted()).toEqual({ pending: false, running: false });
  });

  it('cancel error keeps current flags so the user can retry', () => {
    expect(onCancelError({ pending: false, running: true })).toEqual({
      pending: false,
      running: true
    });
  });

  it('terminal status and poll errors clear running', () => {
    expect(onTaskTerminalStatus()).toEqual({ pending: false, running: false });
    expect(onStatusPollError()).toEqual({ pending: false, running: false });
  });
});
