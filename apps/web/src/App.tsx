import { Navigate, Route, BrowserRouter, Routes } from 'react-router';
import { RequireAuth } from './auth/AuthProvider';
import { AuthCallbackPage, ForgotPasswordPage, LoginPage, ResetPasswordPage, SessionExpiredPage } from './auth/AuthPages';
import { ToastHost } from './components/ToastHost';
import { AccountIndex, AccountLayout, ProfilePage, SecurityPage } from './features/account/AccountPages';
import { AdminIndex, AdminLayout, DeletedIssuesPage, ManageUserPage, UsersPage } from './features/admin/AdminPages';
import { IssuePage } from './features/issues/IssuePage';
import { IssuesListPage } from './features/issues/IssuesListPage';
import { SpaceGeneralPage, SpaceMembersPage, SpaceSettingsIndex, SpaceSettingsLayout } from './features/spaces/SpaceSettings';
import { SpacesPage } from './features/spaces/SpacesPage';
import { AppShell, BareShell } from './layout/AppShell';
import { DevUiPage } from './pages/DevUiPage';
import { NotFoundPage } from './pages/NotFoundPage';

export function App() {
  return (
    <BrowserRouter>
      <ToastHost>
        <Routes>
          <Route path="login" element={<LoginPage />} />
          <Route path="forgot-password" element={<ForgotPasswordPage />} />
          <Route path="reset-password" element={<ResetPasswordPage />} />
          <Route path="session-expired" element={<SessionExpiredPage />} />
          <Route path="auth/callback" element={<AuthCallbackPage />} />
          <Route path="dev/ui" element={<main className="p-8"><DevUiPage /></main>} />

          <Route element={<RequireAuth><BareShell /></RequireAuth>}>
            <Route path="account" element={<AccountLayout />}>
              <Route index element={<AccountIndex />} />
              <Route path="profile" element={<ProfilePage />} />
              <Route path="security" element={<SecurityPage />} />
            </Route>
            <Route path="admin" element={<AdminLayout />}>
              <Route index element={<AdminIndex />} />
              <Route path="users" element={<UsersPage />} />
              <Route path="users/:userId" element={<ManageUserPage />} />
              <Route path="deleted-issues" element={<DeletedIssuesPage />} />
            </Route>
            <Route path="spaces/:spaceKey/settings" element={<SpaceSettingsLayout />}>
              <Route index element={<SpaceSettingsIndex />} />
              <Route path="general" element={<SpaceGeneralPage />} />
              <Route path="members" element={<SpaceMembersPage />} />
            </Route>
          </Route>

          <Route element={<RequireAuth><AppShell /></RequireAuth>}>
            <Route index element={<Navigate to="spaces" replace />} />
            <Route path="spaces" element={<SpacesPage />} />
            <Route path="spaces/:spaceKey/issues" element={<IssuesListPage />} />
            <Route path="issues/:issueKey" element={<IssuePage />} />
            <Route path="search" element={<IssuesListPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </ToastHost>
    </BrowserRouter>
  );
}
