#!/usr/bin/env bash
# Restore drill (make backup-restore-test): download the latest encrypted
# backup, decrypt it with the local private age key, restore it into a
# throwaway Postgres container and prove the data is really there.
# A backup that has never been restored does not count (docs/architecture.md).
set -euo pipefail

SECRETS="$HOME/.config/verbatim-intelligence/prod-secrets.yml"
AGE_KEY="$HOME/.config/verbatim-intelligence/backup-age.key"
BUCKET="yantech-verbatim-backups"
AWS_CLI_IMAGE="amazon/aws-cli@sha256:238583846e731f31c9848dae26c5a560769ff35c4c5368a4cb6be5816683e485"
POSTGRES_IMAGE="postgres:18-alpine@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15"

read_secret() { awk -F': ' -v k="$1" '$1==k {gsub(/"/, "", $2); print $2}' "$SECRETS"; }
AWS_ACCESS_KEY_ID="$(read_secret backup_access_key)"
AWS_SECRET_ACCESS_KEY="$(read_secret backup_secret_key)"
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY

workdir="$(mktemp -d)"
container="verbatim-restore-test-$$"
cleanup() {
  docker rm -f "$container" > /dev/null 2>&1 || true
  rm -rf "$workdir"
}
trap cleanup EXIT

aws() {
  docker run --rm -i -v "$workdir":/work -w /work \
    -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY \
    "$AWS_CLI_IMAGE" --endpoint-url https://s3.fr-par.scw.cloud --region fr-par "$@"
}

latest="$(aws s3 ls "s3://$BUCKET/" | awk '{print $4}' | sort | tail -1)"
[ -n "$latest" ] || { echo "no backup found in s3://$BUCKET" >&2; exit 1; }
echo "latest backup: $latest"

aws s3 cp "s3://$BUCKET/$latest" "/work/backup.dump.age" > /dev/null

docker run --rm -i -v "$workdir":/work -v "$AGE_KEY":/age.key:ro alpine:3.22 \
  sh -c "apk add -q age && age -d -i /age.key -o /work/backup.dump /work/backup.dump.age"

docker run -d --name "$container" -e POSTGRES_PASSWORD=restore-test "$POSTGRES_IMAGE" > /dev/null
until docker exec "$container" pg_isready -U postgres > /dev/null 2>&1; do sleep 1; done

docker exec "$container" createuser -U postgres verbatim
docker exec "$container" createdb -U postgres -O verbatim verbatim
docker cp "$workdir/backup.dump" "$container":/tmp/backup.dump
docker exec "$container" pg_restore -U postgres -d verbatim /tmp/backup.dump

echo "--- restored content"
docker exec "$container" psql -U postgres -d verbatim -Atc \
  "SELECT 'users: ' || count(*) FROM users
   UNION ALL SELECT 'analyses: ' || count(*) FROM analyses
   UNION ALL SELECT 'sessions: ' || count(*) FROM sessions;"
users_count="$(docker exec "$container" psql -U postgres -d verbatim -Atc 'SELECT count(*) FROM users;')"
[ "$users_count" -ge 1 ] || { echo "restore check failed: no users restored" >&2; exit 1; }
echo "RESTORE OK — backup $latest restores into a working database"
