import { createHmac } from 'node:crypto';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NextRequest } from 'next/server';

const revalidateTag = vi.fn();
vi.mock('next/cache', () => ({ revalidateTag: (tag: string) => revalidateTag(tag) }));

import { POST } from './route';
import { tagsForContentEvent } from '@/lib/cms/revalidation';

function req(body: object, headers: Record<string, string>): NextRequest {
  return new NextRequest('http://localhost:3000/api/revalidate', {
    method: 'POST',
    body: JSON.stringify(body),
    headers: { 'content-type': 'application/json', ...headers },
  });
}

function sign(secret: string, body: object): string {
  return 'sha256=' + createHmac('sha256', secret).update(JSON.stringify(body), 'utf8').digest('hex');
}

describe('POST /api/revalidate', () => {
  beforeEach(() => {
    revalidateTag.mockClear();
    delete process.env.REVALIDATE_SECRET;
  });

  it('drops the page, article and type tags for a content event', async () => {
    const body = { id: 1, slug: 'demo/home', contentTypeName: 'standard_page', status: 'Published' };
    const res = await POST(req(body, { 'x-cms-event': 'content.published' }));
    expect(res.status).toBe(200);
    expect(revalidateTag.mock.calls.map((c: unknown[]) => c[0]).sort()).toEqual(
      ['cms-article-demo/home', 'cms-page-demo/home', 'cms-standard-page'].sort(),
    );
  });

  it('drops the nav tag for navigation.updated', async () => {
    const res = await POST(req({ handle: 'primary' }, { 'x-cms-event': 'navigation.updated' }));
    expect(res.status).toBe(200);
    expect(revalidateTag).toHaveBeenCalledWith('cms-primary-nav');
  });

  it('drops the site settings tag for settings.updated (epic #141)', async () => {
    const res = await POST(req({ key: 'site.title', scope: 'Public' }, { 'x-cms-event': 'settings.updated' }));
    expect(res.status).toBe(200);
    expect(revalidateTag).toHaveBeenCalledWith('cms-site-settings');
  });

  it('drops the redirect tag for redirects.updated from the admin API (#169)', async () => {
    const res = await POST(req({ id: 7 }, { 'x-cms-event': 'redirects.updated' }));
    expect(res.status).toBe(200);
    expect(revalidateTag.mock.calls.map((c: unknown[]) => c[0])).toEqual(['cms-redirects']);
  });

  it('also drops both slugs\' pages for a slug-change redirects.updated (#169)', async () => {
    const body = { id: 1, slug: 'new', previousSlug: 'old', contentTypeName: 'standard_page', fromPath: '/pages/old', toPath: '/pages/new' };
    const res = await POST(req(body, { 'x-cms-event': 'redirects.updated' }));
    expect(res.status).toBe(200);
    expect(revalidateTag.mock.calls.map((c: unknown[]) => c[0]).sort()).toEqual(
      ['cms-redirects', 'cms-page-new', 'cms-article-new', 'cms-page-old', 'cms-article-old', 'cms-standard-page'].sort(),
    );
  });

  it('rejects a bad signature when REVALIDATE_SECRET is set', async () => {
    process.env.REVALIDATE_SECRET = 's3cret';
    const body = { slug: 'x' };
    const res = await POST(req(body, { 'x-cms-event': 'content.published', 'x-cms-signature': 'sha256=nope' }));
    expect(res.status).toBe(401);
    expect(revalidateTag).not.toHaveBeenCalled();
  });

  it('accepts a valid HMAC signature', async () => {
    process.env.REVALIDATE_SECRET = 's3cret';
    const body = { slug: 'x', contentTypeName: 'news_article' };
    const res = await POST(req(body, { 'x-cms-event': 'content.unpublished', 'x-cms-signature': sign('s3cret', body) }));
    expect(res.status).toBe(200);
    expect(revalidateTag).toHaveBeenCalledWith('cms-news-article');
  });

  it('ignores unknown events without touching the cache', async () => {
    const res = await POST(req({}, { 'x-cms-event': 'media.uploaded' }));
    expect((await res.json()).revalidated).toBe(false);
    expect(revalidateTag).not.toHaveBeenCalled();
  });

  it('tagsForContentEvent refreshes both listings when the type is unknown', () => {
    expect(tagsForContentEvent({ slug: 'a' })).toEqual(
      expect.arrayContaining(['cms-page-a', 'cms-article-a', 'cms-news-article', 'cms-standard-page']),
    );
  });
});
