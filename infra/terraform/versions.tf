terraform {
  required_version = ">= 1.15"

  required_providers {
    scaleway = {
      source  = "scaleway/scaleway"
      version = "~> 2.78"
    }

    # The yantech.fr DNS zone lives at Hostinger.
    hostinger = {
      source  = "hostinger/hostinger"
      version = "~> 0.1.22"
    }
  }

  # Scaleway Object Storage, S3-compatible. Credentials come from the
  # AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY environment variables set by
  # the make targets (never committed).
  backend "s3" {
    bucket = "yantech-verbatim-tfstate"
    key    = "infra.tfstate"
    region = "fr-par"

    endpoints = {
      s3 = "https://s3.fr-par.scw.cloud"
    }

    skip_credentials_validation = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_s3_checksum            = true
  }
}

provider "scaleway" {
  zone   = var.zone
  region = "fr-par"
}

provider "hostinger" {
  api_token = var.hostinger_api_token
}
