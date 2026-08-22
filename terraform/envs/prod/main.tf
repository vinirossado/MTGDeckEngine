# Production overlay — kept thin so the dev module stays the source of truth.
# Differences from dev: multi-AZ NAT, larger node group, 2× Neptune instances.

terraform {
  required_version = ">= 1.6"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.50" }
  }
}
provider "aws" { region = var.region }

variable "region" {
  type    = string
  default = "eu-north-1"
}
variable "name" {
  type    = string
  default = "mtg-deck-engine-prod"
}

locals {
  tags = {
    Project     = "mtg-deck-engine"
    Environment = "prod"
    ManagedBy   = "terraform"
  }
}

module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "~> 5.8"
  name                 = "${var.name}-vpc"
  cidr                 = "10.43.0.0/16"
  azs                  = ["${var.region}a", "${var.region}b", "${var.region}c"]
  private_subnets      = ["10.43.1.0/24", "10.43.2.0/24", "10.43.3.0/24"]
  public_subnets       = ["10.43.101.0/24", "10.43.102.0/24", "10.43.103.0/24"]
  enable_nat_gateway   = true
  single_nat_gateway   = false
  enable_dns_hostnames = true
  tags                 = local.tags
}

module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "~> 20.20"
  cluster_name    = var.name
  cluster_version = "1.30"
  vpc_id          = module.vpc.vpc_id
  subnet_ids      = module.vpc.private_subnets
  enable_irsa     = true
  eks_managed_node_groups = {
    default = {
      desired_size   = 3
      min_size       = 2
      max_size       = 8
      instance_types = ["t3.medium"]
    }
  }
  tags = local.tags
}

module "neptune" {
  source         = "../../modules/neptune"
  name           = "${var.name}-neptune"
  vpc_id         = module.vpc.vpc_id
  subnet_ids     = module.vpc.private_subnets
  allowed_sg_ids = [module.eks.node_security_group_id]
  instance_class = "db.r5.large"
  instance_count = 2
  tags           = local.tags
}

output "cluster_name"     { value = module.eks.cluster_name }
output "neptune_endpoint" { value = module.neptune.endpoint }
