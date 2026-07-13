output "public_ip" {
  description = "Public IPv4 of the instance — point verbatim.yantech.fr here"
  value       = scaleway_instance_ip.public.address
}

# SMTP settings for the backend (magic links). The password is the secret
# key of the send-only IAM key: read it with
# `make infra-output ARGS='-raw smtp_password'`, never commit it.
output "smtp_host" {
  value = scaleway_tem_domain.verbatim.smtp_host
}

output "smtp_port" {
  value = scaleway_tem_domain.verbatim.smtp_port
}

output "smtp_username" {
  value = scaleway_tem_domain.verbatim.smtps_auth_user
}

output "smtp_password" {
  value     = scaleway_iam_api_key.tem_sender.secret_key
  sensitive = true
}

# Backup job credentials: read them with `output -raw`, they land in the
# prod secrets file (never the repo).
output "backup_access_key" {
  value = scaleway_iam_api_key.backup_writer.access_key
}

output "backup_secret_key" {
  value     = scaleway_iam_api_key.backup_writer.secret_key
  sensitive = true
}
