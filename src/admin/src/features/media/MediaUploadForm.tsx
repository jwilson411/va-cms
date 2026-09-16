/**
 * MediaUploadForm — upload a file into the media library.
 *
 * POST /api/v1/media/upload (multipart/form-data, field "file"); issue #40.
 * The API validates extension/MIME (BRD FR-SECURITY-06) and answers 400 with
 * { error } for rejected files, which is surfaced inline. On success the asset
 * list query is invalidated so the new file appears immediately.
 */

import { useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

const UPLOAD_URL = '/api/v1/media/upload';

interface UploadResult {
  id: number;
  fileName: string;
}

async function uploadFile(file: File): Promise<UploadResult> {
  const body = new FormData();
  body.append('file', file, file.name);
  // No Content-Type header: the browser sets multipart/form-data with the boundary.
  const res = await authorizedFetch(UPLOAD_URL, { method: 'POST', body });
  if (!res.ok) {
    let message = `Upload failed (${res.status})`;
    try {
      const data = (await res.json()) as { error?: string };
      if (data.error) message = data.error;
    } catch {
      /* non-JSON error body */
    }
    throw new Error(message);
  }
  return (await res.json()) as UploadResult;
}

interface MediaUploadFormProps {
  /** Called with the new asset id after a successful upload. */
  onUploaded?: (id: number) => void;
}

export function MediaUploadForm({ onUploaded }: MediaUploadFormProps): JSX.Element {
  const qc = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const mutation = useMutation<UploadResult, Error, File>({
    mutationFn: uploadFile,
    onSuccess: (result) => {
      setMessage({ kind: 'success', text: `Uploaded ${result.fileName}.` });
      setFile(null);
      if (inputRef.current) inputRef.current.value = '';
      void qc.invalidateQueries({ queryKey: ['media-assets'] });
      onUploaded?.(result.id);
    },
    onError: (err) => setMessage({ kind: 'error', text: err.message }),
  });

  return (
    <form
      className="usa-form margin-bottom-3"
      aria-label="Upload media"
      onSubmit={(e) => {
        e.preventDefault();
        if (file) mutation.mutate(file);
      }}
    >
      <div className="display-flex flex-align-end flex-gap-2 flex-wrap">
        <div className="usa-form-group margin-top-0">
          <label className="usa-label margin-top-0" htmlFor="media-upload-file">
            Upload a file
          </label>
          <span className="usa-hint" id="media-upload-hint">
            Images, PDFs and documents up to 100 MB.
          </span>
          <input
            id="media-upload-file"
            ref={inputRef}
            className="usa-file-input"
            type="file"
            aria-describedby="media-upload-hint"
            disabled={mutation.isPending}
            onChange={(e) => {
              setMessage(null);
              setFile(e.target.files?.[0] ?? null);
            }}
          />
        </div>
        <button
          type="submit"
          className="usa-button"
          disabled={!file || mutation.isPending}
        >
          {mutation.isPending ? 'Uploading…' : 'Upload'}
        </button>
      </div>

      {message && (
        <p
          className={message.kind === 'error' ? 'usa-error-message' : 'usa-hint text-green'}
          role={message.kind === 'error' ? 'alert' : 'status'}
          data-testid="media-upload-message"
        >
          {message.text}
        </p>
      )}
    </form>
  );
}
