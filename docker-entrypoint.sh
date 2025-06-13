#!/bin/sh
# entrypoint.sh — generic DACPAC publisher, POSIX shell only

# ── Fail fast ────────────────────────────────────────────────────────────
set -eu
# BusyBox ash supports “pipefail” as an option
set -o pipefail

# ── Helpers & defaults ───────────────────────────────────────────────────
log() {
  # e.g. [2025-06-25T12:34:56Z] message
  printf '[%s] %s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$*"
}

# required
: "${SQL_CONNECTION_STRING:?SQL_CONNECTION_STRING must be set}"
: "${DACPAC_PATH:?DACPAC_PATH must be set}"

# optional
SQLPACKAGE_ARGS="${SQLPACKAGE_ARGS:-/p:DropObjectsNotInSource=false}"
MAX_RETRIES="${MAX_RETRIES:-3}"
RETRY_DELAY="${RETRY_DELAY:-5}"

# ── Pre-check ────────────────────────────────────────────────────────────
if [ ! -f "$DACPAC_PATH" ]; then
  log "ERROR: DACPAC not found at '$DACPAC_PATH'"
  exit 1
fi

# ── Publish with retry loop ──────────────────────────────────────────────
attempt=1
while [ "$attempt" -le "$MAX_RETRIES" ]; do
  log "Attempt $attempt/$MAX_RETRIES: Publishing '$DACPAC_PATH'…"
  if sqlpackage \
      /Action:Publish \
      /SourceFile:"$DACPAC_PATH" \
      /TargetConnectionString:"$SQL_CONNECTION_STRING" \
      $SQLPACKAGE_ARGS
  then
    log "SUCCESS"
    exit 0
  else
    log "WARN: attempt $attempt failed"
    attempt=$((attempt + 1))
    if [ "$attempt" -le "$MAX_RETRIES" ]; then
      log "Retrying in ${RETRY_DELAY}s…"
      sleep "$RETRY_DELAY"
    fi
  fi
done

log "ERROR: Exceeded $MAX_RETRIES attempts; giving up."
exit 1
