/**
 * Pure pending/running transitions for Studio task controls.
 * Locks the historical "stuck spinner on error path" class of bugs.
 */

export interface StudioRunFlags {
  pending: boolean;
  running: boolean;
}

export function beginCreateOrRerun(): StudioRunFlags {
  return { pending: true, running: false };
}

/** Same lock as create/rerun — continue also queues before the worker starts. */
export function beginContinue(): StudioRunFlags {
  return { pending: true, running: false };
}

/** createTask HTTP failed before run was issued. */
export function onCreateTaskError(): StudioRunFlags {
  return { pending: false, running: false };
}

/** continueTask HTTP failed. */
export function onContinueTaskError(): StudioRunFlags {
  return { pending: false, running: false };
}

/** runTask Accepted / completed handshake. */
export function onRunTaskAccepted(): StudioRunFlags {
  return { pending: false, running: true };
}

/** runTask HTTP failed. */
export function onRunTaskError(): StudioRunFlags {
  return { pending: false, running: false };
}

/** cancelTask succeeded — stop the spinner. */
export function onCancelAccepted(): StudioRunFlags {
  return { pending: false, running: false };
}

/**
 * cancelTask failed — keep running so the user can retry.
 * (Matches StudioPage cancelActiveTask error handler.)
 */
export function onCancelError(current: StudioRunFlags): StudioRunFlags {
  return { pending: current.pending, running: current.running };
}

/** Terminal task status from status poll. */
export function onTaskTerminalStatus(): StudioRunFlags {
  return { pending: false, running: false };
}

/** Status poll itself failed. */
export function onStatusPollError(): StudioRunFlags {
  return { pending: false, running: false };
}
