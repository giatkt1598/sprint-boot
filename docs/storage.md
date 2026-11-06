# File storage

Attachments, files placed in rich text, avatars and logos are stored by the provider set in `Storage:Provider`.

| Setting | Meaning |
| --- | --- |
| `Storage:Provider` | `Local` (default) or `S3`. |
| `Storage:Local:Path` | Folder for `Local`. Several replicas must share it. |
| `Storage:S3:Bucket`, `AccessKey`, `SecretKey` | The bucket and the keys of an account that can read, write and delete in it. |
| `Storage:S3:Region` | The AWS region. Optional when `Endpoint` is set. |
| `Storage:S3:Endpoint` | An S3-compatible service instead of AWS, such as `https://<account>.r2.cloudflarestorage.com` or a MinIO address. Addresses are path-style. |
| `Issues:Attachments:MaxFileSizeMb` | Largest attachment or rich text file. Default 2048 (2 GB). |
| `Issues:Uploads:PartSizeKb` | Size of each upload part. Default 16384 (16 MB). With S3, keep it at least 5120 (5 MB). |

## Large files

Proxies in front of Sprint Boot often cap the size of one request: Cloudflare, including Cloudflare Tunnel, stops at
100 MB. So the web app sends attachments and rich text files **in parts**:

1. `POST /api/v1/upload/start` checks that the person may add files to the issue or space and that the size is allowed.
   It returns the list of parts.
2. Each part goes up separately, three at a time:
   - **S3**: straight to object storage, with a presigned `PUT` URL per part (an S3 multipart upload). The file never
     passes through the API or its proxy. URLs last an hour; `POST /api/v1/upload/{id}/renew` gives new ones.
   - **Local**: to `POST /api/v1/upload/{id}/upload-part?number=n` on the API. Each request carries one part only.
3. `POST /api/v1/upload/{id}/complete` joins the parts, checks the stored size, and creates the attachment or the rich
   text file.

A failed part is retried. An upload that still fails is cancelled, and the person can try again. Uploads that are never
completed are cancelled after 24 hours, and what storage holds of them is freed.

The older single-request endpoints (`/api/v1/attachment/upload/{issueId}`, `/api/v1/rich-text-file/upload/{projectId}`)
remain for API clients. They are subject to the proxy's limit.

## Bucket CORS (S3)

Browsers send the parts to the bucket from the Sprint Boot origin, so the bucket must allow it, and must expose `ETag`,
which the web app sends back to complete the upload:

```json
[
  {
    "AllowedOrigins": ["https://sprint.example.com"],
    "AllowedMethods": ["PUT"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3600
  }
]
```

On Cloudflare R2 set this under the bucket's **Settings → CORS policy**. On AWS use **Permissions → Cross-origin
resource sharing**.

If the bucket's public address is also behind Cloudflare, uploads still work: each part is far below the 100 MB limit.

## Development

`docker compose -f deploy/docker/compose.yml --profile s3 up -d` starts LocalStack S3 at `http://localhost:4566` with the
bucket `sprint-boot` and the CORS rules above for `http://localhost:5173`. Run the API with:

```
Storage__Provider=S3
Storage__S3__Endpoint=http://localhost:4566
Storage__S3__Bucket=sprint-boot
Storage__S3__AccessKey=test
Storage__S3__SecretKey=test
```
