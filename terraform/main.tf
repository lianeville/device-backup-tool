terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.5"
    }
  }

  required_version = ">= 1.3.0"
}

provider "aws" {
  region = "us-east-1"
}

# Generate a random string for the bucket name (only lowercase letters and numbers)
resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

# Create S3 bucket
resource "aws_s3_bucket" "unifi_backups" {
  bucket = "unifi-backups-${random_string.suffix.result}"
}

# Create versioning configuration for S3 bucket
resource "aws_s3_bucket_versioning" "unifi_backups_versioning" {
  bucket = aws_s3_bucket.unifi_backups.id

  versioning_configuration {
    status = "Enabled"
  }
}

# Separate resource for server-side encryption configuration
resource "aws_s3_bucket_server_side_encryption_configuration" "encryption" {
  bucket = aws_s3_bucket.unifi_backups.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# S3 Bucket Public Access Block
resource "aws_s3_bucket_public_access_block" "block_public_access" {
  bucket                  = aws_s3_bucket.unifi_backups.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# IAM Policy
resource "aws_iam_policy" "unifi_backups_policy" {
  name        = "unifi-backups"
  description = "Policy to allow putting and deleting objects in the UniFi backups bucket"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Sid      = "AllowS3Actions",
        Effect   = "Allow",
        Action   = ["s3:PutObject", "s3:DeleteObject"],
        Resource = "arn:aws:s3:::${aws_s3_bucket.unifi_backups.bucket}/*"
      }
    ]
  })
}

# IAM User
resource "aws_iam_user" "unifi_backups_user" {
  name = "unifi_backups_user"
}

# Attach Policy to User
resource "aws_iam_user_policy_attachment" "attach_policy" {
  user       = aws_iam_user.unifi_backups_user.name
  policy_arn = aws_iam_policy.unifi_backups_policy.arn
}

# Access Key and Secret for the User
resource "aws_iam_access_key" "unifi_backups_key" {
  user = aws_iam_user.unifi_backups_user.name
}

# Outputs
output "bucket_name" {
  value = aws_s3_bucket.unifi_backups.bucket
}

output "bucket_arn" {
  value = aws_s3_bucket.unifi_backups.arn
}

output "access_key_id" {
  value = aws_iam_access_key.unifi_backups_key.id
}

output "secret_access_key" {
  value     = aws_iam_access_key.unifi_backups_key.secret
  sensitive = true
}
