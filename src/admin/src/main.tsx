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
