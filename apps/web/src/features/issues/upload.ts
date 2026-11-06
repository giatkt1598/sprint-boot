import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';

type Session = components['schemas']['UploadSessionDto'];
type UploadedPart = components['schemas']['UploadedPartDto'];
export type UploadResult = components['schemas']['UploadResultDto'];

export type UploadTarget = { purpose: 0; issueId: string } | { purpose: 1; projectId: string };

/** Bytes sent so far out of the file's size; reported after each part. */
export type UploadProgress = (loaded: number, total: number) => void;

const PARALLEL_PARTS = 3;
const ATTEMPTS = 3;

/** A part that object storage turned away because its presigned URL ran out. */
class ExpiredUrl extends Error {}

const wait = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

/**
 * Sends a file in parts, so no request is larger than a proxy such as Cloudflare accepts. With object storage the parts go
 * straight to presigned URLs; otherwise through the API. A failed part is retried, expired URLs are renewed, and an upload
 * that still fails is abandoned on the server before the error is thrown.
 */
export async function uploadFile(target: UploadTarget, file: File, onProgress?: UploadProgress): Promise<UploadResult> {
  let session: Session = await unwrap(
    apiClient.POST('/api/v1/upload/start', {
      body: { ...target, fileName: file.name, contentType: file.type || null, size: file.size },
    }),
  );
  const id = session.id!;
  const partSize = session.partSize!;
  const sent = new Map<number, number>();
  const report = () => onProgress?.([...sent.values()].reduce((sum, value) => sum + value, 0), file.size);
  let renewing: Promise<void> | null = null;

  // Parts share one renewal, however many of them find their URL expired at once.
  const renew = () =>
    (renewing ??= unwrap(apiClient.POST('/api/v1/upload/{id}/renew', { params: { path: { id } } })).then((renewed) => {
      session = renewed;
      renewing = null;
    }));

  const sendOnce = async (number: number, body: Blob): Promise<string> => {
    const url = session.parts?.find((part) => part.number === number)?.url;
    if (url) {
      const response = await fetch(url, { method: 'PUT', body });
      if (response.status === 403) {
        throw new ExpiredUrl();
      }
      const tag = response.headers.get('ETag');
      if (!response.ok || !tag) {
        throw new Error(response.statusText || 'Upload failed');
      }
      return tag;
    }

    const part = await unwrap(
      apiClient.POST('/api/v1/upload/{id}/upload-part', {
        params: { path: { id }, query: { number } },
        body: { content: body } as never,
        bodySerializer: () => {
          const form = new FormData();
          form.append('content', body, file.name);
          return form;
        },
      }),
    );
    return part.eTag!;
  };

  const send = async (number: number): Promise<UploadedPart> => {
    const body = file.slice((number - 1) * partSize, Math.min(file.size, number * partSize));
    for (let attempt = 1; ; attempt++) {
      try {
        const eTag = await sendOnce(number, body);
        sent.set(number, body.size);
        report();
        return { number, eTag };
      } catch (failure) {
        if (attempt >= ATTEMPTS) {
          throw failure;
        }
        if (failure instanceof ExpiredUrl) {
          await renew();
        } else {
          await wait(500 * 2 ** attempt);
        }
      }
    }
  };

  try {
    const numbers = (session.parts ?? []).map((part) => part.number!);
    const parts: UploadedPart[] = [];
    let next = 0;
    const worker = async () => {
      while (next < numbers.length) {
        parts.push(await send(numbers[next++]!));
      }
    };
    report();
    await Promise.all(Array.from({ length: Math.min(PARALLEL_PARTS, numbers.length) }, worker));
    return await unwrap(apiClient.POST('/api/v1/upload/{id}/complete', { params: { path: { id } }, body: { parts } }));
  } catch (failure) {
    // Frees what storage holds of the parts; the error that matters is the one above.
    void apiClient.POST('/api/v1/upload/{id}/abort', { params: { path: { id } } }).catch(() => undefined);
    throw failure;
  }
}
