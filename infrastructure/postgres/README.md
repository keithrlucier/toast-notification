# PostgreSQL — TOASTDATA1

Database operational artifacts for `toastrevival` on TOASTDATA1 (`100.52.96.67` SSH / `172.26.3.164` private). PostgreSQL 16.13 on Ubuntu 24.04 LTS.

## What's here

```
infrastructure/postgres/
├── toast-pg-backup.sh        ← daily pg_dump script
├── toast-pg-backup.service   ← systemd one-shot service
├── toast-pg-backup.timer     ← systemd timer (daily 02:00 UTC)
└── README.md                 ← this file (procedure + restore drill)
```

These are snapshots; the authoritative copies live on TOASTDATA1 at `/usr/local/bin/toast-pg-backup.sh` and `/etc/systemd/system/toast-pg-backup.{service,timer}`. When the live config changes, snapshot back to this directory in the same commit that documents the change. Drift between the repo snapshot and the live config is a documentation bug.

## Backup rhythm

- **Schedule**: `OnCalendar=02:00 UTC` with `RandomizedDelaySec=5min`. Off-peak relative to US business hours; `Persistent=true` catches missed runs after a reboot.
- **Format**: `pg_dump --format=custom` — gzip-compressed binary with a parseable TOC. Supports parallel restore via `pg_restore -j N`.
- **Retention**: 14 days rolling. `find -name '*.dump' -mtime +14 -delete`. With the database at ~10 MB and ~5x compression, 14 dumps run ~30 MB on disk — trivial footprint on the 38 GB TOASTDATA1 root.
- **Verification**: every dump passes `pg_restore --list` before the script atomically renames `.partial` → `.dump`. Corrupt dumps never reach the visible inventory.
- **Where dumps live**: `/opt/toast/backups/toastrevival-YYYYMMDD-HHMMSSZ.dump`, owned by `postgres:postgres`, mode `0750` on the directory and `0644` on the files.

## Off-box copy (M9)

Currently dumps sit on the same box as the database — survives accidental DELETE / bad migration, does NOT survive whole-box loss. Lightsail automatic snapshots (per `Docs/ToastRevival/DEPLOY.md`) cover whole-box loss with a 7-day retention, so the disaster-recovery posture is:

| Failure mode | Recovery path | RPO |
|---|---|---|
| Bad migration / DELETE / table corruption | `pg_restore` from `/opt/toast/backups/` | 24h |
| Box loss (instance termination, region issue) | Lightsail snapshot restore | 7d |
| Both above on same day | (gap) | — |

Closing the third row is M9 polish — an off-box copy to S3 or to TOASTWEB1 over the private network. Not required pre-revenue.

## Manual run

```bash
ssh ubuntu@100.52.96.67
sudo systemctl start toast-pg-backup.service
sudo journalctl -u toast-pg-backup -n 20 --no-pager
sudo ls -la /opt/toast/backups/
```

## Status check

```bash
ssh ubuntu@100.52.96.67
sudo systemctl list-timers toast-pg-backup.timer --no-pager
sudo systemctl status toast-pg-backup.service --no-pager
```

`list-timers` shows when the next run is scheduled and when the last one fired. `status` shows the most recent run's exit code.

## Restore drill (run this monthly)

A backup that's never restored is a backup you don't trust. The drill:

```bash
ssh ubuntu@100.52.96.67
sudo bash -c '
  LATEST=$(ls -t /opt/toast/backups/toastrevival-*.dump | head -1)
  echo "drill against: $LATEST"

  # Restore into a throwaway database — does not touch toastrevival.
  sudo -u postgres createdb toastrevival_drill
  sudo -u postgres pg_restore --dbname=toastrevival_drill --jobs=2 "$LATEST"

  # Verify schema + at least one row in a known table.
  sudo -u postgres psql -d toastrevival_drill -c "
    SELECT COUNT(*) AS migration_count FROM \"__EFMigrationsHistory\";
    SELECT COUNT(*) AS tenant_count    FROM \"Tenants\";
  "

  # Tear down.
  sudo -u postgres dropdb toastrevival_drill
  echo "drill ok: latest dump restores cleanly"
'
```

A passing drill produces non-zero migration and tenant counts and exits 0. Document any drill that fails into FIX-LIST immediately — a backup that doesn't restore is worse than no backup because it gives false confidence.

## Logs

- **systemd journal**: `sudo journalctl -u toast-pg-backup`. Persisted in `/var/log/journal/`. Full backup history.
- **Failure visibility**: when the service exits non-zero, the journal carries `pg_dump` / `pg_restore` stderr verbatim. No external log aggregation today; M9 polish.

## Common failure modes

| Symptom | Likely cause | Fix |
|---|---|---|
| `could not connect to server` | postgres service not running on TOASTDATA1 | `sudo systemctl start postgresql` |
| `permission denied for database` | `pg_hba.conf` rejected the local socket connection | postgres-OS-user runs the script; ensure `postgres` peer auth in `pg_hba.conf` |
| `pg_restore --list` exit 1 | dump truncated mid-write (out of disk?) | check `df -h /`; partial removed automatically |
| Timer drift | clock skew or paused VM | `systemctl reload-or-restart toast-pg-backup.timer` |

## Sync workflow (live → repo)

When the live script or units change on TOASTDATA1, snapshot back:

```bash
scp -i Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem \
    ubuntu@100.52.96.67:/usr/local/bin/toast-pg-backup.sh \
    infrastructure/postgres/

ssh -i Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem \
    ubuntu@100.52.96.67 \
    'sudo cat /etc/systemd/system/toast-pg-backup.service' \
    > infrastructure/postgres/toast-pg-backup.service

ssh -i Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem \
    ubuntu@100.52.96.67 \
    'sudo cat /etc/systemd/system/toast-pg-backup.timer' \
    > infrastructure/postgres/toast-pg-backup.timer
```

Push repo → live in the reverse direction; `systemctl daemon-reload` after any unit-file change.
