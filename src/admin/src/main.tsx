import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './context/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { ContentTypeBrowserPage } from './pages/ContentTypeBrowserPage';
import { ContentEntryListPage } from './features/contentEntries';
import { MediaLibraryPage } from './features/media';
import { SearchAnalyticsPage } from './features/searchAnalytics';
import { UserListPage, UserDetailPage } from './features/users';
import { AuditLogPage } from './features/audit';
import { NavigationEditorPage } from './features/navigation';

const queryClient = new QueryClient();

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            {/* Public login page — entry point for the OIDC flow */}
            <Route path="/login" element={<LoginPage />} />

            {/* All admin routes are protected — ProtectedRoute handles the redirect */}
            <Route
              path="/*"
              element={
                <ProtectedRoute>
                  <Routes>
                    {/* Issue #26: Admin content type browser (FR-SCHEMA-06) */}
                    <Route path="/admin/content-types" element={<ContentTypeBrowserPage />} />
                    {/* Issue #42: Media library browser */}
                    <Route path="/admin/media" element={<MediaLibraryPage />} />
                    {/* Issue #29: Admin content entry list (FR-AUTH-01) */}
                    <Route path="/admin/content" element={<ContentEntryListPage />} />
                    {/* Issue #51: Search analytics full page (FR-SEARCH-06) */}
                    <Route path="/admin/search/analytics" element={<SearchAnalyticsPage />} />
                    {/* Issue #56: User directory and role assignment admin UI (FR-USERS-03/04) */}
                    <Route path="/admin/users" element={<UserListPage />} />
                    <Route path="/admin/users/:userId" element={<UserDetailPage />} />
                    {/* Issue #57: Audit log viewer (FR-USERS-06) */}
                    <Route path="/admin/audit" element={<AuditLogPage />} />
                    {/* Issue #46: Navigation menu CRUD and drag-and-drop editor (FR-NAV-01, FR-NAV-03) */}
                    <Route path="/admin/navigation" element={<NavigationEditorPage />} />
                    <Route path="*" element={<DashboardPage />} />
                  </Routes>
                </ProtectedRoute>
              }
            />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  </React.StrictMode>,
);
