#!/usr/bin/env bash
# entrypoint.sh — deploy a DACPAC to Azure SQL with best practices

set -euo pipefail
IFS=$'\n\t'

# --- Configuration (can be overridden via env) ---
: "${SQL_CONNECTION_STRING:?Environment variable SQL_CONNECTION_STRING must be set}"
DACPAC_FILE="${DACPAC_FILE:-MyDatabase.dacpac}"
# Additional sqlpackage parameters, e.g. "/p:DropObjectsNotInSource=false"
SQLPACKAGE_ARGS="${SQLPACKAGE_ARGS:-/p:DropObjectsNotInSource=false}"
MAX_RETRIES="${MAX_RETRIES:-5}"
RETRY_DELAY="${RETRY_DELAY:-10}"  # seconds

# --- Logging helper ---
log() {
  echo "[$(date -u +"%Y-%m-%dT%H:%M:%SZ")] $*"
}

# --- Validate DACPAC exists ---
if [[ ! -f "$DACPAC_FILE" ]]; then
  log "ERROR: DACPAC file '$DACPAC_FILE' not found."
  exit 1
fi

# --- Deployment loop with retry on transient failures ---
attempt=1
while true; do
  log "Attempt $attempt/$MAX_RETRIES: publishing '$DACPAC_FILE' to Azure SQL"
  if sqlpackage \
      /Action:Publish \
      /SourceFile:"$DACPAC_FILE" \
      /TargetConnectionString:"$SQL_CONNECTION_STRING" \
      $SQLPACKAGE_ARGS
  then
    log "SUCCESS: DACPAC deployed."
    exit 0
  else
    log "WARN: Deployment attempt $attempt failed."
    if (( attempt >= MAX_RETRIES )); then
      log "ERROR: Exceeded max retries ($MAX_RETRIES). Aborting."
      exit 1
    fi
    attempt=$(( attempt + 1 ))
    log "Retrying in $RETRY_DELAY seconds..."
    sleep "$RETRY_DELAY"
  fi
done
