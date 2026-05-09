#!/usr/bin/env bash
# Toast Notification — daily pg_dump of the toastrevival database.
# Runs as postgres via systemd timer. See /etc/systemd/system/toast-pg-backup.{service,timer}.
#
# Format: --format=custom (compressed binary, supports parallel restore via pg_restore -j).
# Retention: 14 days rolling.
# Verification: pg_restore --list parses the dump's TOC header — fails non-zero on corruption.
set -euo pipefail

DB="toastrevival"
DIR="/opt/toast/backups"
TS="$(date -u +%Y%m%d-%H%M%SZ)"
OUT="${DIR}/${DB}-${TS}.dump"
TMP="${OUT}.partial"

echo "[$(date -u +%FT%TZ)] backup start: ${DB} -> ${OUT}"

# Dump to a .partial file so a crash mid-write never produces a half-finished
# .dump that retention or restore would later treat as good.
pg_dump --format=custom --file="${TMP}" --dbname="${DB}"

# Verify the dump is readable by pg_restore (parses the TOC header).
if ! pg_restore --list "${TMP}" > /dev/null; then
    echo "[$(date -u +%FT%TZ)] ERROR: pg_restore --list failed on ${TMP}" >&2
    rm -f "${TMP}"
    exit 1
fi

# Atomic publish — the .dump filename only exists after verification passes.
mv "${TMP}" "${OUT}"
SIZE="$(du -h "${OUT}" | cut -f1)"
echo "[$(date -u +%FT%TZ)] backup ok: ${OUT} (${SIZE})"

# Retention: 14 days rolling. find -delete is atomic per file; safe to run
# while a future backup is still writing to .partial (different name pattern).
find "${DIR}" -name "${DB}-*.dump" -type f -mtime +14 -delete

# Print current backup inventory for journal visibility.
echo "[$(date -u +%FT%TZ)] retention swept; current inventory:"
ls -la "${DIR}"/${DB}-*.dump 2>/dev/null || echo "(no dumps present)"
