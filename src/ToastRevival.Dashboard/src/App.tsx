import { lazy, Suspense } from 'react';
import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { DOCS_PATHS } from './routes/docsRoutes';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Register from './pages/Register';
import SetPassword from './pages/SetPassword';
import CheckEmail from './pages/CheckEmail';
import VerifySms from './pages/VerifySms';
import ForgotPassword from './pages/ForgotPassword';
import ResetPassword from './pages/ResetPassword';
import Dashboard from './pages/Dashboard';
import Devices from './pages/Devices';
import InstallAgent from './pages/InstallAgent';
import Templates from './pages/Templates';
import Compose from './pages/Compose';
import History from './pages/History';
import Moderation from './pages/Moderation';
import ApiKeys from './pages/ApiKeys';
import Users from './pages/Users';
import Analytics from './pages/Analytics';
import TenantSettings from './pages/TenantSettings';
import Assets from './pages/Assets';
import AuditLog from './pages/AuditLog';
import Billing from './pages/Billing';
import Onboarding from './pages/Onboarding';
import TrialRequests from './pages/TrialRequests';
import PlatformTenants from './pages/PlatformTenants';
import PlatformTenantDetail from './pages/PlatformTenantDetail';
import PlatformUsers from './pages/PlatformUsers';

// Marketing chunks — lazy so the dashboard bundle doesn't pull in the marketing CSS / pages
// for already-authenticated users, and the public marketing bundle doesn't pull in Recharts.
const MarketingLayout = lazy(() => import('./components/marketing/MarketingLayout'));
const Home = lazy(() => import('./pages/marketing/Home'));
const Pricing = lazy(() => import('./pages/marketing/Pricing'));
const DocsLayout = lazy(() => import('./components/marketing/DocsLayout'));
const DocsIndex = lazy(() => import('./pages/marketing/docs/DocsIndex'));
const DocsGettingStarted = lazy(() => import('./pages/marketing/docs/DocsGettingStarted'));
const DocsStore = lazy(() => import('./pages/marketing/docs/DocsStore'));
const DocsIntune = lazy(() => import('./pages/marketing/docs/DocsIntune'));
const DocsRmm = lazy(() => import('./pages/marketing/docs/DocsRmm'));
const DocsApi = lazy(() => import('./pages/marketing/docs/DocsApi'));
const DocsModeration = lazy(() => import('./pages/marketing/docs/DocsModeration'));
const Security = lazy(() => import('./pages/marketing/Security'));
const Llms = lazy(() => import('./pages/marketing/Llms'));
const Privacy = lazy(() => import('./pages/marketing/legal/Privacy'));
const Terms = lazy(() => import('./pages/marketing/legal/Terms'));

function MarketingFallback() {
  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'var(--bg-primary)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}
    >
      <div className="spinner" />
    </div>
  );
}

/**
 * Root index — the path / serves the public marketing Home for anonymous
 * visitors, and redirects authenticated users to /dashboard. Sidebar's
 * "Dashboard" link still uses to="/" and is resolved through this redirect.
 */
function RootIndex() {
  const { user, loading } = useAuth();
  if (loading) return <MarketingFallback />;
  if (user) return <Navigate to="/dashboard" replace />;
  return (
    <Suspense fallback={<MarketingFallback />}>
      <Home />
    </Suspense>
  );
}

const router = createBrowserRouter([
  // Auth surfaces
  { path: '/login',         element: <Login /> },
  { path: '/register',      element: <Register /> },
  { path: '/set-password',    element: <SetPassword /> },
  { path: '/verify-sms',     element: <VerifySms /> },
  { path: '/check-email',    element: <CheckEmail /> },
  { path: '/forgot-password', element: <ForgotPassword /> },
  { path: '/reset-password',  element: <ResetPassword /> },
  { path: '/onboarding',    element: <ProtectedRoute><Onboarding /></ProtectedRoute> },

  // Public marketing surfaces — single SPA, MarketingLayout chrome
  {
    element: (
      <Suspense fallback={<MarketingFallback />}>
        <MarketingLayout />
      </Suspense>
    ),
    children: [
      { path: '/',          element: <RootIndex /> },
      { path: '/pricing',   element: <Suspense fallback={<MarketingFallback />}><Pricing /></Suspense> },
      { path: '/security',       element: <Suspense fallback={<MarketingFallback />}><Security /></Suspense> },
      { path: '/llms',           element: <Suspense fallback={<MarketingFallback />}><Llms /></Suspense> },
      { path: '/legal/privacy',  element: <Suspense fallback={<MarketingFallback />}><Privacy /></Suspense> },
      { path: '/legal/terms',    element: <Suspense fallback={<MarketingFallback />}><Terms /></Suspense> },
      {
        element: (
          <Suspense fallback={<MarketingFallback />}>
            <DocsLayout />
          </Suspense>
        ),
        children: [
          { path: DOCS_PATHS.index,          element: <Suspense fallback={<MarketingFallback />}><DocsIndex /></Suspense> },
          { path: DOCS_PATHS.gettingStarted, element: <Suspense fallback={<MarketingFallback />}><DocsGettingStarted /></Suspense> },
          { path: DOCS_PATHS.deployStore,   element: <Suspense fallback={<MarketingFallback />}><DocsStore /></Suspense> },
          { path: DOCS_PATHS.deployIntune,  element: <Suspense fallback={<MarketingFallback />}><DocsIntune /></Suspense> },
          { path: DOCS_PATHS.deployRmm,     element: <Suspense fallback={<MarketingFallback />}><DocsRmm /></Suspense> },
          { path: DOCS_PATHS.api,           element: <Suspense fallback={<MarketingFallback />}><DocsApi /></Suspense> },
          { path: DOCS_PATHS.moderation,    element: <Suspense fallback={<MarketingFallback />}><DocsModeration /></Suspense> },
        ],
      },
    ],
  },

  // Authenticated dashboard surfaces
  {
    element: (
      <ProtectedRoute>
        <Layout />
      </ProtectedRoute>
    ),
    children: [
      { path: '/dashboard',   element: <Dashboard /> },
      { path: '/analytics',   element: <Analytics /> },
      { path: '/devices',     element: <Devices /> },
      {
        path: '/devices/install',
        element: (
          <ProtectedRoute requireAdmin>
            <InstallAgent />
          </ProtectedRoute>
        ),
      },
      { path: '/templates',   element: <Templates /> },
      { path: '/compose',     element: <Compose /> },
      { path: '/assets',      element: <Assets /> },
      { path: '/history',     element: <History /> },
      {
        path: '/moderation',
        element: (
          <ProtectedRoute requireAdmin>
            <Moderation />
          </ProtectedRoute>
        ),
      },
      {
        path: '/settings/api-keys',
        element: (
          <ProtectedRoute requireAdmin>
            <ApiKeys />
          </ProtectedRoute>
        ),
      },
      {
        path: '/settings/tenant',
        element: (
          <ProtectedRoute requireAdmin>
            <TenantSettings />
          </ProtectedRoute>
        ),
      },
      {
        path: '/users',
        element: (
          <ProtectedRoute requireAdmin>
            <Users />
          </ProtectedRoute>
        ),
      },
      {
        path: '/audit',
        element: (
          <ProtectedRoute requireAdmin>
            <AuditLog />
          </ProtectedRoute>
        ),
      },
      {
        path: '/billing',
        element: (
          <ProtectedRoute requireAdmin>
            <Billing />
          </ProtectedRoute>
        ),
      },
      {
        path: '/system/trial-requests',
        element: (
          <ProtectedRoute requireAdmin>
            <TrialRequests />
          </ProtectedRoute>
        ),
      },
      {
        path: '/system/tenants',
        element: (
          <ProtectedRoute requireAdmin>
            <PlatformTenants />
          </ProtectedRoute>
        ),
      },
      {
        path: '/system/tenants/:id',
        element: (
          <ProtectedRoute requireAdmin>
            <PlatformTenantDetail />
          </ProtectedRoute>
        ),
      },
      {
        path: '/system/users',
        element: (
          <ProtectedRoute requireAdmin>
            <PlatformUsers />
          </ProtectedRoute>
        ),
      },
    ],
  },

  { path: '*', element: <Navigate to="/" replace /> },
]);

export default function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  );
}
