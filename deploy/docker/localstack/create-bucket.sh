#!/bin/sh
# The bucket Sprint Boot stores files in, with the CORS rules browsers need to PUT parts to presigned URLs.
awslocal s3 mb s3://sprint-boot
awslocal s3api put-bucket-cors --bucket sprint-boot --cors-configuration '{
  "CORSRules": [{
    "AllowedOrigins": ["http://localhost:5173"],
    "AllowedMethods": ["PUT"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3600
  }]
}'
