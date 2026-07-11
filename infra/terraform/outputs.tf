output "public_ip" {
  description = "Public IPv4 of the instance — point verbatim.yantech.fr here"
  value       = scaleway_instance_ip.public.address
}
