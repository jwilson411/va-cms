/**
 * lib/cms/content.test.ts
 *
 * Tests for CMS content fetching utilities.
 * Issue #58 — Standard Page template.
 * Issue #59 — News Article template.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  fetchStandardPage,
  fetchNewsArticle,
  extractH2Sections,
  injectH2Ids,
} from './content';

// ---------------------------------------------------------------------------
// extractH2Sections
// ---------------------------------------------------------------------------

describe('extractH2Sections', () => {
  it('returns empty array for empty string', () => {
    expect(extractH2Sections('')).toEqual([]);
  });

  it('returns empty array when no H2 headings present', () => {
    const html = '<p>Some text</p><h3>Sub-heading</h3>';
    expect(extractH2Sections(html)).toEqual([]);
  });

  it('extracts H2 sections with text', () => {
    const html = `
      <h2>Overview</h2>
      <p>Content</p>
      <h2>Eligibility</h2>
      <p>More content</p>
    `;
    const sections = extractH2Sections(html);
    expect(sections).toHaveLength(2);
    expect(sections[0].text).toBe('Overview');
    expect(sections[1].text).toBe('Eligibility');
  });

  it('generates slug-style ids from H2 text when no id attr', () => {
    const html = '<h2>How to Apply</h2>';
    const sections = extractH2Sections(html);
    expect(sections[0].id).toBe('how-to-apply');
  });

  it('uses existing id attribute when present', () => {
    const html = '<h2 id="existing-id">Section</h2>';
    const sections = extractH2Sections(html);
    expect(sections[0].id).toBe('existing-id');
  });

  it('handles H2 with extra attributes', () => {
    const html = '<h2 class="some-class" id="my-section">My Section</h2>';
    const sections = extractH2Sections(html);
    expect(sections[0].id).toBe('my-section');
    expect(sections[0].text).toBe('My Section');
  });

  it('returns 3 sections from typical page body', () => {
    const html = `
      <h2>Introduction</h2><p>Para</p>
      <h2>Benefits</h2><p>Para</p>
      <h2>How to Apply</h2><p>Para</p>
    `;
    expect(extractH2Sections(html)).toHaveLength(3);
  });
});

// ---------------------------------------------------------------------------
// injectH2Ids
// ---------------------------------------------------------------------------

describe('injectH2Ids', () => {
  it('returns unchanged string when no H2 tags', () => {
    const html = '<p>text</p>';
    expect(injectH2Ids(html)).toBe(html);
  });

  it('injects id on H2 without existing id', () => {
    const html = '<h2>Overview</h2>';
    const result = injectH2Ids(html);
    expect(result).toContain('id="overview"');
  });

  it('does not duplicate id when H2 already has one', () => {
    const html = '<h2 id="already-set">Section</h2>';
    const result = injectH2Ids(html);
    // Should not add a second id
    const idCount = (result.match(/\bid=/g) ?? []).length;
    expect(idCount).toBe(1);
    expect(result).toContain('id="already-set"');
  });

  it('converts spaces and special chars to hyphens in generated id', () => {
    const html = '<h2>How to Apply!</h2>';
    const result = injectH2Ids(html);
    expect(result).toContain('id="how-to-apply"');
  });

  it('injects ids on multiple H2 headings', () => {
    const html = '<h2>First Section</h2><h2>Second Section</h2>';
    const result = injectH2Ids(html);
    expect(result).toContain('id="first-section"');
    expect(result).toContain('id="second-section"');
  });
});

// ---------------------------------------------------------------------------
// fetchStandardPage
// ---------------------------------------------------------------------------

describe('fetchStandardPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('returns null on 404 response', async () => {
    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
    } as Response);

    const result = await fetchStandardPage('nonexistent-page');
    expect(result).toBeNull();
  });

  it('returns null on network error', async () => {
    global.fetch = vi.fn().mockRejectedValueOnce(new Error('Network error'));

    const result = await fetchStandardPage('test-page');
    expect(result).toBeNull();
  });

  it('returns the parsed content entry on success', async () => {
    const mockEntry = {
      id: 1,
      contentTypeId: 1,
      contentTypeName: 'standard_page',
      slug: 'about',
      locale: 'en-US',
      status: 'Published',
      fields: {
        title: 'About VA',
        body: '## Overview\n\nContent here.',
        renderedBody: '<h2>Overview</h2><p>Content here.</p>',
      },
      publishedAt: '2026-09-01T00:00:00Z',
    };

    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockEntry,
    } as unknown as Response);

    const result = await fetchStandardPage('about');
    expect(result).not.toBeNull();
    expect(result?.fields.title).toBe('About VA');
    expect(result?.slug).toBe('about');
  });

  it('returns null on non-404 error status', async () => {
    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => ({}),
    } as Response);

    const result = await fetchStandardPage('test-page');
    expect(result).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// fetchNewsArticle
// ---------------------------------------------------------------------------

describe('fetchNewsArticle', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('returns null on 404 response', async () => {
    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
    } as Response);

    const result = await fetchNewsArticle('nonexistent-article');
    expect(result).toBeNull();
  });

  it('returns null on network error', async () => {
    global.fetch = vi.fn().mockRejectedValueOnce(new Error('Network error'));

    const result = await fetchNewsArticle('test-article');
    expect(result).toBeNull();
  });

  it('returns the parsed content entry on success', async () => {
    const mockEntry = {
      id: 2,
      contentTypeId: 2,
      contentTypeName: 'news_article',
      slug: 'va-expands-services',
      locale: 'en-US',
      status: 'Published',
      fields: {
        title: 'VA Expands Services',
        summary: 'A summary.',
        body: '## Overview\n\nContent.',
        renderedBody: '<h2>Overview</h2><p>Content.</p>',
        author: 'Jane Smith',
        publishDate: '2026-09-15T00:00:00Z',
        featuredImage: {
          storageUrl: '/media/hero.jpg',
          altText: 'A VA facility',
        },
        topics: [
          { slug: 'mental-health', name: 'Mental Health' },
        ],
      },
      publishedAt: '2026-09-15T00:00:00Z',
    };

    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockEntry,
    } as unknown as Response);

    const result = await fetchNewsArticle('va-expands-services');
    expect(result).not.toBeNull();
    expect(result?.fields.title).toBe('VA Expands Services');
    expect(result?.fields.author).toBe('Jane Smith');
    expect(result?.fields.featuredImage?.altText).toBe('A VA facility');
    expect(result?.fields.topics).toHaveLength(1);
  });

  it('returns null on non-404 error status', async () => {
    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 503,
      json: async () => ({}),
    } as Response);

    const result = await fetchNewsArticle('test-article');
    expect(result).toBeNull();
  });

  it('uses ?type=news_article query parameter in the request URL', async () => {
    global.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
    } as Response);

    await fetchNewsArticle('my-article');

    const calledUrl = (global.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0] as string;
    expect(calledUrl).toContain('type=news_article');
    expect(calledUrl).toContain('my-article');
  });
});
