import { MetaProvider } from '@solidjs/meta';
import { Router, Route, Navigate } from '@solidjs/router';
import { lazy } from 'solid-js';
import LandingLayout from './components/LandingLayout';
import DashboardLayout from './components/DashboardLayout';

const Landing = lazy(() => import('./routes/Landing'));
const Pricing = lazy(() => import('./routes/Pricing'));
const SelfHosting = lazy(() => import('./routes/SelfHosting'));
const TermsOfService = lazy(() => import('./routes/TermsOfService'));
const PrivacyPolicy = lazy(() => import('./routes/PrivacyPolicy'));
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
    <MetaProvider>
    <Router>
      {/* Landing pages */}
      <Route path="/" component={LandingLayout}>
        <Route path="/" component={Landing} />
        <Route path="/pricing" component={Pricing} />
        <Route path="/docs/self-hosting" component={SelfHosting} />
        <Route path="/terms" component={TermsOfService} />
        <Route path="/privacy" component={PrivacyPolicy} />
      </Route>

      {/* Auth pages */}
      <Route path="/login" component={Login} />
      <Route path="/register" component={Register} />
      <Route path="/forgot-password" component={ForgotPassword} />
      <Route path="/reset-password" component={ResetPassword} />

      {/* Dashboard (protected) */}
      <Route path="/dashboard" component={DashboardLayout}>
        <Route path="/" component={Overview} />
        <Route path="/create" component={CreateInstance} />
        <Route path="/instances/:id" component={InstanceDetail} />
        <Route path="/billing" component={Billing} />
        <Route path="/account" component={Account} />
      </Route>

      <Route path="*" component={() => <Navigate href="/" />} />
    </Router>
    </MetaProvider>
  );
}
