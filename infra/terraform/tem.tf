# Transactional e-mail (magic links): Scaleway TEM on verbatim.yantech.fr.
# The DNS zone lives at Hostinger, so the SPF/DKIM/MX/DMARC records TEM
# requires are declared there, with values exported by the TEM domain —
# one declarative chain: domain -> records -> validation.

resource "scaleway_tem_domain" "verbatim" {
  name       = "verbatim.yantech.fr"
  accept_tos = true
}

resource "hostinger_dns_record" "tem_spf" {
  zone  = "yantech.fr"
  name  = "verbatim"
  type  = "TXT"
  value = scaleway_tem_domain.verbatim.spf_value
  ttl   = 3600
}

# DKIM and DMARC are NOT declared here, though they exist (posed by the
# initial apply, verified by the domain validation below): the Hostinger API
# returns long TXT values split into quoted chunks, which the provider
# (0.1.22) fails to match back — it then believes the record was never
# created and errors out. If they ever need re-creating, PUT them on
# /api/dns/v1/zones/yantech.fr with the values exported by this TEM domain.

resource "hostinger_dns_record" "tem_mx" {
  zone  = "yantech.fr"
  name  = "verbatim"
  type  = "MX"
  value = scaleway_tem_domain.verbatim.mx_config
  ttl   = 3600
}

resource "scaleway_tem_domain_validation" "verbatim" {
  domain_id = scaleway_tem_domain.verbatim.id
  region    = "fr-par"
  timeout   = 600

  depends_on = [
    hostinger_dns_record.tem_spf,
    hostinger_dns_record.tem_mx,
  ]
}

# Send-only credentials: the SMTP password is this key's secret, scoped to
# e-mail sending (least privilege — no domain administration).
resource "scaleway_iam_application" "tem_sender" {
  name = "verbatim-tem-sender"
}

resource "scaleway_iam_policy" "tem_sender" {
  name           = "verbatim-tem-sender"
  application_id = scaleway_iam_application.tem_sender.id

  rule {
    project_ids          = [scaleway_tem_domain.verbatim.project_id]
    permission_set_names = ["TransactionalEmailEmailFullAccess"]
  }
}

resource "scaleway_iam_api_key" "tem_sender" {
  application_id = scaleway_iam_application.tem_sender.id
  description    = "SMTP password for transactional e-mail (verbatim.yantech.fr)"
  # Organization policy requires an expiry. Rotating it is: bump the date,
  # apply, update the SMTP password in prod secrets, redeploy.
  expires_at = "2027-07-01T00:00:00Z"
}
