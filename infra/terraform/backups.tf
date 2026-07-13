# Postgres backups (the non-negotiable prerequisite of slice 3, see
# docs/architecture.md): encrypted dumps land in a dedicated, versioned
# bucket. The server only ever holds write credentials and the *public*
# encryption key — a compromised server can neither read nor silently
# rewrite history (versioning keeps every object generation for 30 days).

resource "scaleway_object_bucket" "backups" {
  name = "yantech-verbatim-backups"

  versioning {
    enabled = true
  }

  lifecycle_rule {
    id      = "expire-old-backups"
    enabled = true
    expiration {
      days = 30
    }
    noncurrent_version_expiration {
      noncurrent_days = 30
    }
  }
}

# Write-side credentials, dedicated to the backup job.
resource "scaleway_iam_application" "backup_writer" {
  name = "verbatim-backup-writer"
}

resource "scaleway_iam_policy" "backup_writer" {
  name           = "verbatim-backup-writer"
  application_id = scaleway_iam_application.backup_writer.id

  rule {
    project_ids          = [var.project_id]
    permission_set_names = ["ObjectStorageObjectsWrite", "ObjectStorageObjectsRead", "ObjectStorageBucketsRead"]
  }
}

resource "scaleway_iam_api_key" "backup_writer" {
  application_id = scaleway_iam_application.backup_writer.id
  description    = "S3 credentials for encrypted Postgres backups"
  # The S3 gateway routes requests by the key's default project: without
  # this, PutObject on the project's bucket is AccessDenied.
  default_project_id = var.project_id
  expires_at     = "2027-07-01T00:00:00Z"
}
