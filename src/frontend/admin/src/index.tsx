import { render } from 'solid-js/web';
import '@fontsource-variable/figtree';
import { App } from './App';
import './index.css';

const root = document.getElementById('root');

if (!root) {
  throw new Error('Root element not found');
}

render(() => <App />, root);
