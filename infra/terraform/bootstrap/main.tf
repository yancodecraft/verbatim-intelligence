# One-time bootstrap: the Object Storage bucket holding the Terraform state
# of the main configuration. Chicken-and-egg by nature, so this tiny config
# keeps a LOCAL state (gitignored, loss-tolerable: the bucket can be
# re-imported with `terraform import`).
#
#   make infra-bootstrap
terraform {
  required_version = ">= 1.15"

  required_providers {
    scaleway = {
      source  = "scaleway/scaleway"
      version = "~> 2.78"
    }
  }
}

provider "scaleway" {
  region = "fr-par"
}

resource "scaleway_object_bucket" "tfstate" {
  name = "yantech-verbatim-tfstate"

  versioning {
    enabled = true
  }
}
