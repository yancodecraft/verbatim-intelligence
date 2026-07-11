resource "scaleway_iam_ssh_key" "deploy" {
  name       = "verbatim-deploy"
  public_key = var.ssh_public_key
}

resource "scaleway_instance_ip" "public" {}

resource "scaleway_instance_security_group" "web" {
  name                    = "verbatim-web"
  inbound_default_policy  = "drop"
  outbound_default_policy = "accept"

  dynamic "inbound_rule" {
    for_each = [22, 80, 443]
    content {
      action = "accept"
      port   = inbound_rule.value
    }
  }
}

resource "scaleway_instance_server" "app" {
  name              = "verbatim-intelligence"
  type              = var.server_type
  image             = "ubuntu_noble"
  ip_id             = scaleway_instance_ip.public.id
  security_group_id = scaleway_instance_security_group.web.id

  root_volume {
    size_in_gb = 40
  }
}
