-- O5 (docs/security-review.md): the application must not connect to Postgres as
-- a superuser. The migrate one-shot (superuser) owns the schema; this script,
-- run as a one-shot right after it on every deploy, creates and refreshes a
-- least-privilege role that the backend and ai-worker use instead.
--
-- Idempotent by design: safe to replay, and the ALTER ROLE ... PASSWORD doubles
-- as the app-role rotation (change APP_DB_PASSWORD, redeploy — no initdb gotcha,
-- unlike the superuser password). The password is passed in as the psql variable
-- :app_password and is never written to the repo.
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'verbatim_app') THEN
    CREATE ROLE verbatim_app LOGIN;
  END IF;
END
$$;

ALTER ROLE verbatim_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
  PASSWORD :'app_password';

-- DML only: the app reads and writes rows, nothing more. No DDL, no COPY, no
-- reach into other roles' catalogs — a SQL injection or a compromised service
-- can touch data, not seize the cluster.
GRANT CONNECT ON DATABASE verbatim TO verbatim_app;
GRANT USAGE ON SCHEMA public TO verbatim_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO verbatim_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO verbatim_app;

-- Tables a future migration adds inherit the same grants without a manual
-- rerun (migrate creates them as verbatim, the owner named here).
ALTER DEFAULT PRIVILEGES FOR ROLE verbatim IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO verbatim_app;
ALTER DEFAULT PRIVILEGES FOR ROLE verbatim IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO verbatim_app;
