terraform {
  required_version = ">= 1.6"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.50"
    }
  }
  # Configure your own state backend before applying in shared environments:
  # backend "s3" { bucket = "mtg-tfstate", key = "dev/terraform.tfstate", region = "eu-north-1", dynamodb_table = "mtg-tflocks" }
}

provider "aws" { region = var.region }

variable "region"  { type = string  default = "eu-north-1" }
variable "name"    { type = string  default = "mtg-deck-engine-dev" }

# Common tags applied to everything created here.
locals {
  tags = {
    Project     = "mtg-deck-engine"
    Environment = "dev"
    ManagedBy   = "terraform"
  }
}

# ---------- VPC ----------------------------------------------------
module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "~> 5.8"

  name                 = "${var.name}-vpc"
  cidr                 = "10.42.0.0/16"
  azs                  = ["${var.region}a", "${var.region}b", "${var.region}c"]
  private_subnets      = ["10.42.1.0/24", "10.42.2.0/24", "10.42.3.0/24"]
  public_subnets       = ["10.42.101.0/24", "10.42.102.0/24", "10.42.103.0/24"]
  enable_nat_gateway   = true
  single_nat_gateway   = true   # dev: 1 NAT to save cost; prod: false
  enable_dns_hostnames = true
  tags                 = local.tags
}

# ---------- EKS ----------------------------------------------------
module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "~> 20.20"

  cluster_name    = var.name
  cluster_version = "1.30"
  vpc_id          = module.vpc.vpc_id
  subnet_ids      = module.vpc.private_subnets

  enable_irsa = true

  eks_managed_node_groups = {
    default = {
      desired_size = 2
      min_size     = 1
      max_size     = 4
      instance_types = ["t3.small"]
    }
  }
  tags = local.tags
}

# ---------- Neptune ------------------------------------------------
module "neptune" {
  source         = "../../modules/neptune"
  name           = "${var.name}-neptune"
  vpc_id         = module.vpc.vpc_id
  subnet_ids     = module.vpc.private_subnets
  allowed_sg_ids = [module.eks.node_security_group_id]
  instance_class = "db.t4g.medium"
  instance_count = 1     # 2+ for prod, across AZs
  tags           = local.tags
}

# ---------- IRSA for the API ServiceAccount ------------------------
data "aws_iam_policy_document" "neptune_full" {
  statement {
    sid       = "NeptuneDataApi"
    effect    = "Allow"
    actions   = ["neptune-db:*"]
    resources = ["arn:aws:neptune-db:${var.region}:*:${module.neptune.cluster_resource_id}/*"]
  }
}

resource "aws_iam_policy" "neptune" {
  name   = "${var.name}-neptune"
  policy = data.aws_iam_policy_document.neptune_full.json
}

module "irsa" {
  source  = "terraform-aws-modules/iam/aws//modules/iam-role-for-service-accounts-eks"
  version = "~> 5.39"

  role_name = "${var.name}-api"

  oidc_providers = {
    eks = {
      provider_arn               = module.eks.oidc_provider_arn
      namespace_service_accounts = ["mtg-deck-engine:mtg-deck-engine"]
    }
  }

  role_policy_arns = {
    neptune = aws_iam_policy.neptune.arn
  }
  tags = local.tags
}

# ---------- Outputs ------------------------------------------------
output "cluster_name"      { value = module.eks.cluster_name }
output "neptune_endpoint"  { value = module.neptune.endpoint }
output "irsa_role_arn"     { value = module.irsa.iam_role_arn }
