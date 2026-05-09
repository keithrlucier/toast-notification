import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Register from './pages/Register';
import Dashboard from './pages/Dashboard';
import Devices from './pages/Devices';
import Templates from './pages/Templates';
import Compose from './pages/Compose';
import History from './pages/History';
import Moderation from './pages/Moderation';
import ApiKeys from './pages/ApiKeys';
import Users from './pages/Users';
import Analytics from './pages/Analytics';
import TenantSettings from './pages/TenantSettings';
import Assets from './pages/Assets';

const router = createBrowserRouter([
  { path: '/login',    element: <Login /> },
  { path: '/register', element: <Register /> },
  {
    element: (
      <ProtectedRoute>
        <Layout />
      </ProtectedRoute>
    ),
    children: [
      { index: true,          element: <Dashboard /> },
      { path: '/analytics',   element: <Analytics /> },
      { path: '/devices',     element: <Devices /> },
      { path: '/templates',   element: <Templates /> },
      { path: '/compose',     element: <Compose /> },
      { path: '/assets',       element: <Assets /> },
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
