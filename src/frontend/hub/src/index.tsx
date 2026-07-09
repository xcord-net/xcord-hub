import { render } from 'solid-js/web';
import '@fontsource-variable/figtree';
import '@fontsource-variable/schibsted-grotesk';
import '@fontsource/ibm-plex-mono/400.css';
import '@fontsource/ibm-plex-mono/500.css';
import App from './App';
import './index.css';
import { installCsrfFetchInterceptor } from './utils/csrf';

// Install CSRF header on every same-origin state-changing fetch BEFORE any
// component code runs. The backend rejects cookie-authenticated POST/PUT/PATCH/
// DELETE without this header.
installCsrfFetchInterceptor();

const root = document.getElementById('root');

if (root) {
  render(() => <App />, root);
}
