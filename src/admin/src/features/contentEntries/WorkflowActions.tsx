/**
 * WorkflowActions — status badge + transition buttons for a content entry.
 *
 * Issue #37 (BRD FR-WORKFLOW-01): drives the API state machine
 *   Draft → InReview (submit-review)   InReview → Approved (approve)
 *   InReview → Draft (return, comment) Approved/Draft → Published (publish)
 *   Published → Approved (unpublish)
 * The API answers 422 with a plain-language message when a transition is not
 * permitted; that message is shown inline.
 */

import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authorizedFetch } from '../../lib/authorizedFetch';

const CONTENT_API = '/api/v1/content';

/** Subset of GET /api/v1/content/{id} the workflow panel and edit route need. */
export interface EntryStatus {
  status: string;
  slug: string;
  contentTypeName: string | null;
  publishedVersionId: number | null;
  latestVersionId: number | null;
}

type Transition = 'submit-review' | 'approve' | 'return' | 'publish' | 'unpublish';

const TRANSITION_LABELS: Record<Transition, string> = {
  'submit-review': 'Submit for review',
  approve: 'Approve',
  return: 'Return to draft',
  publish: 'Publish',
  unpublish: 'Unpublish',
};

/** Which buttons make sense for a given status (the API is the authority). */
function transitionsFor(status: string): Transition[] {
  switch (status) {
    case 'Draft':     return ['submit-review', 'publish'];
    case 'InReview':  return ['approve', 'return'];
    case 'Approved':  return ['publish'];
    case 'Published': return ['publish', 'unpublish'];
    default:          return [];
  }
}

async function readError(res: Response): Promise<string> {
  try {
    const data = (await res.json()) as { error?: string };
    if (data.error) return data.error;
  } catch {
    /* non-JSON body */
  }
  return `Request failed (${res.status})`;
}

export function useEntryStatus(entryId: number | undefined) {
  return useQuery<EntryStatus>({
    queryKey: ['content-entry-status', entryId],
    queryFn: async () => {
      const res = await authorizedFetch(`${CONTENT_API}/${entryId}`);
      if (!res.ok) throw new Error(await readError(res));
      return (await res.json()) as EntryStatus;
    },
    enabled: entryId !== undefined,
  });
}

interface WorkflowActionsProps {
  entryId: number;
  /** Disable while the form is saving so a transition never races an edit. */
  disabled?: boolean;
}

export function WorkflowActions({ entryId, disabled = false }: WorkflowActionsProps): JSX.Element {
  const queryClient = useQueryClient();
  const { data, isLoading } = useEntryStatus(entryId);
  const [error, setError] = useState<string | null>(null);
  const [returnComment, setReturnComment] = useState('');

  const mutation = useMutation<void, Error, Transition>({
    mutationFn: async (transition) => {
      const init: RequestInit = { method: 'POST' };
      if (transition === 'return') {
        init.headers = { 'Content-Type': 'application/json' };
        init.body = JSON.stringify({ comment: returnComment });
      }
      const res = await authorizedFetch(`${CONTENT_API}/${entryId}/${transition}`, init);
      if (!res.ok) throw new Error(await readError(res));
    },
    onMutate: () => setError(null),
    onSuccess: () => {
      setReturnComment('');
      void queryClient.invalidateQueries({ queryKey: ['content-entry-status', entryId] });
      void queryClient.invalidateQueries({ queryKey: ['content-entries'] });
      void queryClient.invalidateQueries({ queryKey: ['content-versions', entryId] });
    },
    onError: (err) => setError(err.message),
  });

  if (isLoading || !data) {
    return <p className="usa-hint">Loading status…</p>;
  }

  const transitions = transitionsFor(data.status);
  const busy = disabled || mutation.isPending;
  const hasUnpublishedChanges =
    data.status === 'Published' &&
    data.latestVersionId !== null &&
    data.latestVersionId !== data.publishedVersionId;

  return (
    <section className="usa-card margin-top-3" aria-labelledby="workflow-heading">
      <div className="usa-card__header">
        <h2 id="workflow-heading" className="usa-card__heading">
          Workflow
        </h2>
      </div>
      <div className="usa-card__body">
        <p>
          Status:{' '}
          <span
            className={`usa-tag${data.status === 'Published' ? ' usa-tag--green' : ''}`}
            data-testid="workflow-status"
          >
            {data.status}
          </span>
          {hasUnpublishedChanges && (
            <span className="usa-hint margin-left-1">
              (saved changes not yet published — publish again to update the live page)
            </span>
          )}
        </p>

        {transitions.includes('return') && (
          <div className="usa-form-group">
            <label className="usa-label" htmlFor="workflow-return-comment">
              Comment for the author (required to return)
            </label>
            <textarea
              id="workflow-return-comment"
              className="usa-textarea"
              value={returnComment}
              onChange={(e) => setReturnComment(e.target.value)}
              disabled={busy}
            />
          </div>
        )}

        <ul className="usa-button-group">
          {transitions.map((t) => (
            <li key={t} className="usa-button-group__item">
              <button
                type="button"
                className={`usa-button${t === 'unpublish' || t === 'return' ? ' usa-button--outline' : ''}`}
                disabled={busy || (t === 'return' && returnComment.trim() === '')}
                onClick={() => mutation.mutate(t)}
              >
                {TRANSITION_LABELS[t]}
              </button>
            </li>
          ))}
        </ul>

        {error && (
          <p className="usa-error-message" role="alert">
            {error}
          </p>
        )}
      </div>
    </section>
  );
}
