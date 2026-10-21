# Codex Quota Battery maintenance

This is an independent public Git repository. The owner authorizes synchronizing
completed component updates to GitHub. Keep the widget small, transparent and
draggable. Only support platforms actually tested.

After implementing a requested update:
1. Update VERSION, plugin/installer/server versions, CHANGELOG and README.
2. Run `python release.py check`; verify the desktop when relevant.
3. Review the public-file allowlist. Never publish runtime JSON, real thread IDs,
   named pipes, credentials, machine-specific paths, logs or private project files.
4. Run `python release.py sync --message "describe the completed change"`.
5. Verify and report the remote commit. If authentication is missing, preserve
   the local commit and explicitly report that remote sync remains pending.

Never force-push, auto-upload every saved file, or silently change approval and
model settings. Auto-resume must be opt-in for new installs. Only observed active
turns ending failed with explicit official quota-exhaustion evidence qualify.
Interrupted/completed/unknown failures never qualify. Keep the final state check
and persistent deduplication before sending. Never auto-approve a request.

Promotional materials must label simulated flows and synthetic narration. Do not
promise extra quota or guaranteed recovery. Do not imitate a specific creator.
