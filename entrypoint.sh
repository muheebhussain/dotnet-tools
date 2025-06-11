#!/usr/bin/env bash
# deploy‐entrypoint.sh — generic DACPAC publisher

set -euo pipefail
IFS=$'\n\t'

# required env
: "${SQL_CONNECTION_STRING:?Env SQL_CONNECTION_STRING is required}"
: "${DACPAC_FILE:?Env DACPAC_FILE is required}"

# optional env with defaults
SQLPACKAGE_ARGS="${SQLPACKAGE_ARGS:-/p:DropObjectsNotInSource=false}"
MAX_RETRIES="${MAX_RETRIES:-3}"
RETRY_DELAY="${RETRY_DELAY:-5}"

log() { echo "[$(date -u +'%Y-%m-%dT%H:%M:%SZ')] $*"; }

if [[ ! -f "$DACPAC_FILE" ]]; then
  log "ERROR: '$DACPAC_FILE' not found"
  exit 1
fi

for ((i=1; i<=MAX_RETRIES; i++)); do
  log "Publish attempt $i of $MAX_RETRIES..."
  if sqlpackage \
       /Action:Publish \
       /SourceFile:"$DACPAC_FILE" \
       /TargetConnectionString:"$SQL_CONNECTION_STRING" \
       $SQLPACKAGE_ARGS; then
    log "SUCCESS"
    exit 0
  else
    log "FAILED (attempt $i)"
    [[ $i -lt MAX_RETRIES ]] && { log "Retrying in ${RETRY_DELAY}s…"; sleep $RETRY_DELAY; } || break
  fi
done

log "ERROR: all retries exhausted"; exit 1
