import { Navigate, Outlet, Route, BrowserRouter, Routes } from 'react-router';
import { RequireAuth } from './auth/AuthProvider';
import { AuthCallbackPage, AcceptInvitationPage, ForgotPasswordPage, LoginPage, ResetPasswordPage, SessionExpiredPage } from './auth/AuthPages';
import { ErrorBoundary } from './components/ErrorBoundary';
import { ToastHost } from './components/ToastHost';
import { McpDocsPage, TokensPage } from './features/account/TokenPages';
import { AccountIndex, AccountLayout, ProfilePage, SecurityPage } from './features/account/AccountPages';
import { AdminIndex, AdminLayout, DeletedIssuesPage, ManageUserPage, UsersPage } from './features/admin/AdminPages';
import { GitConnectionsPage, GitRepositoriesPage } from './features/admin/GitPages';
import { EmailSettingsPage } from './features/admin/EmailSettingsPage';
import { WorkflowDesignerPage } from './features/workflows/WorkflowDesignerPage';
import { HomePage } from './features/spaces/HomePage';
import { InvitationsPage } from './features/admin/InvitationPages';
import { SsoPage, SsoProviderPage } from './features/admin/SsoPages';
import { GlobalIssueTemplatesPage } from './features/admin/GlobalIssueTemplatesPage';
import { IssueLinkTypesPage } from './features/admin/IssueLinkTypesPage';
import { StatusesPage, WorkflowsPage } from './features/admin/WorkflowPages';
import { BacklogPage } from './features/agile/BacklogPage';
import { BoardPage } from './features/agile/BoardPage';
import { ClosedSprintPage } from './features/agile/ClosedSprintPage';
import { EpicsPage } from './features/agile/EpicsPage';
import { IssuePage } from './features/issues/IssuePage';
import { EditSchemePage, NotificationPreferencesPage, NotificationSchemesPage, NotificationsPage } from './features/notifications/NotificationPages';
import { IssuesListPage } from './features/issues/IssuesListPage';
import { SpaceGeneralPage, SpaceMembersPage, SpaceSettingsIndex, SpaceSettingsLayout } from './features/spaces/SpaceSettings';
import { SpaceBoardPage } from './features/spaces/SpaceBoardPage';
import { SpacesPage } from './features/spaces/SpacesPage';
import { SpaceGuard } from './features/spaces/SpaceGuard';
import { SpaceWorkflowsPage } from './features/spaces/SpaceWorkflowsPage';
import { SpaceIssueTemplatesPage } from './features/spaces/IssueTemplatesPage';
import { AssistantSettingsPage } from './features/admin/AssistantSettingsPage';
import { AssistantPage } from './features/assistant/AssistantPage';
import { AssistantHost } from './features/assistant/AssistantPanel';
import { AppShell, BareShell } from './layout/AppShell';
import { DevUiPage } from './pages/DevUiPage';
import { NotFoundPage } from './pages/NotFoundPage';

export function App() {
  return (
    <BrowserRouter>
      <ToastHost>
        <ErrorBoundary>
        <Routes>
          <Route path="login" element={<LoginPage />} />
          <Route path="forgot-password" element={<ForgotPasswordPage />} />
          <Route path="reset-password" element={<ResetPasswordPage />} />
          <Route path="accept-invitation" element={<AcceptInvitationPage />} />
          <Route path="session-expired" element={<SessionExpiredPage />} />
          <Route path="auth/callback" element={<AuthCallbackPage />} />
          <Route path="dev/ui" element={<main className="p-8"><DevUiPage /></main>} />

          {/* One assistant for every signed-in page, so moving between shells keeps the chat open. */}
          <Route element={<RequireAuth><AssistantHost><Outlet /></AssistantHost></RequireAuth>}>
            <Route element={<BareShell />}>
              <Route path="workflows/:workflowId" element={<WorkflowDesignerPage />} />
              <Route path="account" element={<AccountLayout />}>
                <Route index element={<AccountIndex />} />
                <Route path="profile" element={<ProfilePage />} />
                <Route path="security" element={<SecurityPage />} />
                <Route path="tokens" element={<TokensPage />} />
                <Route path="mcp-docs" element={<McpDocsPage />} />
                <Route path="notifications" element={<NotificationPreferencesPage />} />
              </Route>
              <Route path="admin" element={<AdminLayout />}>
                <Route index element={<AdminIndex />} />
                <Route path="spaces" element={<SpacesPage />} />
                <Route path="users" element={<UsersPage />} />
                <Route path="users/:userId" element={<ManageUserPage />} />
                <Route path="invitations" element={<InvitationsPage />} />
                <Route path="sso" element={<SsoPage />} />
                <Route path="sso/:providerId" element={<SsoProviderPage />} />
                <Route path="email" element={<EmailSettingsPage />} />
                <Route path="ai" element={<AssistantSettingsPage />} />
                <Route path="workflows" element={<WorkflowsPage />} />
                <Route path="issue-link-types" element={<IssueLinkTypesPage />} />
                <Route path="issue-templates" element={<GlobalIssueTemplatesPage />} />
                <Route path="notification-schemes" element={<NotificationSchemesPage />} />
                <Route path="notification-schemes/:schemeId" element={<EditSchemePage />} />
                <Route path="statuses" element={<StatusesPage />} />
                <Route path="deleted-issues" element={<DeletedIssuesPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
              <Route path="spaces/:spaceKey/settings" element={<SpaceSettingsLayout />}>
                <Route index element={<SpaceSettingsIndex />} />
                <Route path="general" element={<SpaceGeneralPage />} />
                <Route path="members" element={<SpaceMembersPage />} />
                <Route path="board" element={<SpaceBoardPage />} />
                <Route path="workflows" element={<SpaceWorkflowsPage />} />
                <Route path="issue-templates" element={<SpaceIssueTemplatesPage />} />
                <Route path="git" element={<GitConnectionsPage />} />
                <Route path="git/:connectionId" element={<GitRepositoriesPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Route>

            <Route element={<AppShell />}>
              <Route index element={<HomePage />} />
              <Route path="spaces" element={<Navigate to="/" replace />} />
              <Route path="spaces/:spaceKey" element={<SpaceGuard />}>
                <Route index element={<Navigate to="issues" replace />} />
                <Route path="backlog" element={<BacklogPage />} />
                <Route path="board" element={<BoardPage />} />
                <Route path="epics" element={<EpicsPage />} />
                <Route path="issues" element={<IssuesListPage />} />
                <Route path="sprints/:sprintId" element={<ClosedSprintPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
              <Route path="issues/:issueKey" element={<IssuePage />} />
              <Route path="search" element={<IssuesListPage />} />
              <Route path="notifications" element={<NotificationsPage />} />
              <Route path="assistant" element={<AssistantPage />} />
              <Route path="assistant/:conversationId" element={<AssistantPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
        </Routes>
        </ErrorBoundary>
      </ToastHost>
    </BrowserRouter>
  );
}
