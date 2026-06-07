# Amazon Neptune cluster — RDF/SPARQL mode (the default engine).
# Lives in private subnets; only reachable from the EKS nodes' security group.

variable "name"                { type = string }
variable "vpc_id"              { type = string }
variable "subnet_ids"          { type = list(string) }
variable "allowed_sg_ids"      { type = list(string) }
variable "instance_class"      { type = string  default = "db.t4g.medium" }
variable "instance_count"      { type = number  default = 1 }
variable "engine_version"      { type = string  default = "1.3.2.0" }
variable "tags"                { type = map(string)  default = {} }

resource "aws_security_group" "neptune" {
  name        = "${var.name}-neptune"
  description = "Neptune cluster ${var.name}"
  vpc_id      = var.vpc_id
  tags        = var.tags
}

resource "aws_security_group_rule" "neptune_in" {
  for_each                 = toset(var.allowed_sg_ids)
  type                     = "ingress"
  security_group_id        = aws_security_group.neptune.id
  source_security_group_id = each.value
  protocol                 = "tcp"
  from_port                = 8182
  to_port                  = 8182
  description              = "SPARQL endpoint from allowed SGs"
}

resource "aws_neptune_subnet_group" "this" {
  name        = "${var.name}-subnets"
  subnet_ids  = var.subnet_ids
  tags        = var.tags
}

resource "aws_neptune_cluster_parameter_group" "this" {
  family = "neptune1.3"
  name   = "${var.name}-cluster"
  parameter {
    name  = "neptune_enable_audit_log"
    value = "1"
  }
}

resource "aws_neptune_cluster" "this" {
  cluster_identifier                  = var.name
  engine                              = "neptune"
  engine_version                      = var.engine_version
  vpc_security_group_ids              = [aws_security_group.neptune.id]
  neptune_subnet_group_name           = aws_neptune_subnet_group.this.name
  neptune_cluster_parameter_group_name = aws_neptune_cluster_parameter_group.this.name
  iam_database_authentication_enabled = true
  storage_encrypted                   = true
  skip_final_snapshot                 = true   # set to false in prod, name the snapshot
  apply_immediately                   = true
  tags                                = var.tags
}

resource "aws_neptune_cluster_instance" "this" {
  count                = var.instance_count
  identifier           = "${var.name}-${count.index}"
  cluster_identifier   = aws_neptune_cluster.this.id
  instance_class       = var.instance_class
  engine               = "neptune"
  apply_immediately    = true
  tags                 = var.tags
}

output "endpoint"          { value = aws_neptune_cluster.this.endpoint }
output "reader_endpoint"   { value = aws_neptune_cluster.this.reader_endpoint }
output "cluster_resource_id" { value = aws_neptune_cluster.this.cluster_resource_id }
output "security_group_id" { value = aws_security_group.neptune.id }
