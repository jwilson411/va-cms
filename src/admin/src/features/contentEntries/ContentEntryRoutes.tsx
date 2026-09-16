/**
 * Route adapters that connect react-router URLs to <ContentEntryFormPage>.
 *
 *   /admin/content/new/:contentTypeName  → create mode
 *   /admin/content/:entryId/edit         → edit mode (looks up the entry's
 *                                          content type name first, since the
 *                                          form needs it to load the schema)
 */

import { useNavigate, useParams } from 'react-router-dom';
import { ContentEntryFormPage } from './ContentEntryFormPage';
import { useEntryStatus } from './WorkflowActions';

const LIST_PATH = '/admin/content';

export function ContentEntryCreateRoute(): JSX.Element {
  const { contentTypeName = '' } = useParams<{ contentTypeName: string }>();
  const navigate = useNavigate();

  return (
    <ContentEntryFormPage
      contentTypeName={contentTypeName}
      onCreated={(id) => navigate(`${LIST_PATH}/${id}/edit`, { replace: true })}
      onCancel={() => navigate(LIST_PATH)}
    />
  );
}

export function ContentEntryEditRoute(): JSX.Element {
  const { entryId: entryIdParam } = useParams<{ entryId: string }>();
  const navigate = useNavigate();
  const entryId = Number(entryIdParam);
  const { data, isLoading, error } = useEntryStatus(Number.isFinite(entryId) ? entryId : undefined);

  if (!Number.isFinite(entryId)) {
    return <NotFound message="Invalid content entry id." />;
  }
  if (isLoading) {
    return (
      <main id="main-content" className="grid-container">
        <p>Loading entry…</p>
      </main>
    );
  }
  if (error || !data?.contentTypeName) {
    return <NotFound message={error?.message ?? `Content entry ${entryId} was not found.`} />;
  }

  return (
    <ContentEntryFormPage
      contentTypeName={data.contentTypeName}
      entryId={entryId}
      onCancel={() => navigate(LIST_PATH)}
    />
  );
}

function NotFound({ message }: { message: string }): JSX.Element {
  return (
    <main id="main-content" className="grid-container">
      <div className="usa-alert usa-alert--error" role="alert">
        <div className="usa-alert__body">
          <h1 className="usa-alert__heading">Content entry not found</h1>
          <p className="usa-alert__text">{message}</p>
        </div>
      </div>
    </main>
  );
}
