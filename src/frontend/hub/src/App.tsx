import { Router, Route, Navigate } from '@solidjs/router';
import { lazy } from 'solid-js';
import HubGuard from './components/HubGuard';
import AppShell from './components/AppShell';
import LandingLayout from './components/LandingLayout';

const Landing = lazy(() => import('./routes/Landing'));
const Pricing = lazy(() => import('./routes/Pricing'));
const SelfHosting = lazy(() => import('./routes/SelfHosting'));
const Login = lazy(() => import('./routes/auth/Login'));
const Register = lazy(() => import('./routes/auth/Register'));
const ForgotPassword = lazy(() => import('./routes/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('./routes/auth/ResetPassword'));
const Overview = lazy(() => import('./routes/dashboard/Overview'));
const CreateInstance = lazy(() => import('./routes/dashboard/CreateInstance'));
const InstanceDetail = lazy(() => import('./routes/dashboard/InstanceDetail'));
const Billing = lazy(() => import('./routes/dashboard/Billing'));
const Account = lazy(() => import('./routes/dashboard/Account'));

export default function App() {
  return (
    <Router>
      <Route path="/" component={() => <LandingLayout><Landing /></LandingLayout>} />
      <Route path="/pricing" component={() => <LandingLayout><Pricing /></LandingLayout>} />
      <Route path="/docs/self-hosting" component={() => <LandingLayout><SelfHosting /></LandingLayout>} />
      <Route path="/login" component={Login} />
      <Route path="/register" component={Register} />
      <Route path="/forgot-password" component={ForgotPassword} />
      <Route path="/reset-password" component={ResetPassword} />
      <Route path="/dashboard" component={() => <HubGuard><AppShell><Overview /></AppShell></HubGuard>} />
      <Route path="/dashboard/create" component={() => <HubGuard><AppShell><CreateInstance /></AppShell></HubGuard>} />
      <Route path="/dashboard/instances/:id" component={() => <HubGuard><AppShell><InstanceDetail /></AppShell></HubGuard>} />
      <Route path="/dashboard/billing" component={() => <HubGuard><AppShell><Billing /></AppShell></HubGuard>} />
      <Route path="/dashboard/account" component={() => <HubGuard><AppShell><Account /></AppShell></HubGuard>} />
      <Route path="*" component={() => <Navigate href="/" />} />
    </Router>
  );
}
