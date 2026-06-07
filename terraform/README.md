# MTG Deck Engine — AWS Infrastructure

Terraform skeleton that provisions:

- A VPC (3 AZs, public + private subnets).
- An **EKS** cluster with one managed node group.
- An **Amazon Neptune** cluster (RDF/SPARQL mode) in the private subnets.
- IAM **IRSA** role for the API ServiceAccount so the pods can call Neptune
  (and SSM Parameter Store for secrets) without static credentials.
- Security groups wiring the EKS nodes' SG into Neptune's port 8182.

This is a **skeleton, not push-button** — it gets you 90% there in HCL but
you'll want to review state-backend config (S3 + DynamoDB), tags, naming, and
secret sourcing before `terraform apply` in any real account.

## Layout

```
terraform/
├── modules/
│   ├── eks/      ← upstream terraform-aws-modules/eks composition
│   └── neptune/  ← Neptune cluster + parameter group + security group
└── envs/
    ├── dev/      ← entry point: backend.tf, main.tf, variables.tf
    └── prod/
```

## Apply (dev)

```bash
cd terraform/envs/dev
terraform init
terraform plan -var "region=eu-north-1"
terraform apply
```

## Cost shape (very rough)

| Resource | Order-of-magnitude |
|---|---|
| EKS control plane | ~$73/mo |
| Neptune db.t4g.medium | ~$130/mo per instance |
| NAT Gateway (1) | ~$33/mo + traffic |
| ALB | ~$20/mo + LCU |

Cheap dev posture: 1× t4g.medium Neptune, 1× t3.small EKS node, single AZ NAT.
For a production-grade Neptune cluster, run at least 2 instances across 2 AZs.

## After the apply

1. Update kubeconfig: `aws eks update-kubeconfig --name mtg-deck-engine-dev`
2. Install the AWS Load Balancer Controller (eksctl or Helm chart).
3. Deploy the app:
   ```bash
   helm install mtg ../../deploy/helm/mtg-deck-engine \
     --namespace mtg-deck-engine --create-namespace \
     --set serviceAccount.irsaRoleArn=<terraform output -raw irsa_role_arn> \
     --set sparql.queryEndpoint=https://<terraform output -raw neptune_endpoint>:8182/sparql \
     --set sparql.updateEndpoint=https://<terraform output -raw neptune_endpoint>:8182/sparql
   ```
