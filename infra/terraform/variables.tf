variable "zone" {
  description = "Scaleway zone hosting the instance"
  type        = string
  default     = "fr-par-1"
}

variable "server_type" {
  description = "Instance type — DEV1-M (3 vCPU, 4 GB) fits the walking skeleton"
  type        = string
  default     = "DEV1-M"
}

variable "ssh_public_key" {
  description = "Public key granted SSH access to the instance"
  type        = string
}
