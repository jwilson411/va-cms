/**
 * AuthedImage — renders an image from GET /api/v1/media/serve/{id} for a signed-in user.
 *
 * A bare <img src> cannot carry the Bearer token, and since #158 the serve endpoint
 * only answers anonymously for assets that published content references. Library
 * thumbnails and previews therefore fetch the bytes with authorizedFetch and show
 * them through a blob: URL, which is revoked when the image unmounts or changes.
 */
import { useEffect, useState } from 'react';
import { authorizedFetch } from '../../lib/authorizedFetch';

type AuthedImageProps = Omit<React.ImgHTMLAttributes<HTMLImageElement>, 'src'> & {
  /** Media asset id to load. */
  assetId: number;
  /** Optional rendition, e.g. "webp". */
  variant?: string;
};

export function AuthedImage({ assetId, variant, alt, ...rest }: AuthedImageProps): JSX.Element {
  const [src, setSrc] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    setSrc(null);
    setFailed(false);

    const query = variant ? `?variant=${encodeURIComponent(variant)}` : '';
    authorizedFetch(`/api/v1/media/serve/${assetId}${query}`)
      .then(async (res) => {
        if (!res.ok) throw new Error(`serve ${res.status}`);
        const blob = await res.blob();
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setSrc(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [assetId, variant]);

  if (failed) {
    return (
      <span role="img" aria-label={alt ?? 'Image unavailable'} style={{ fontSize: '2rem' }}>
        🖼️
      </span>
    );
  }

  // Keep the element (and its test id / dimensions) in the tree while loading.
  return <img src={src ?? undefined} alt={alt} {...rest} />;
}
