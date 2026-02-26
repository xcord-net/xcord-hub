import { A } from '@solidjs/router';

export default function SelfHosting() {
  return (
    <article class="max-w-3xl mx-auto px-6 py-20 text-xcord-landing-text">
      <h1 class="text-4xl font-bold text-white mb-2">Self-Hosting Guide</h1>
      <p class="text-xcord-landing-text-muted mb-12">
        Deploy your own xcord instance — you own your data, your keys, and your infrastructure.
      </p>

      {/* Prerequisites */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">Prerequisites</h2>
        <ul class="space-y-2 text-sm">
          <li class="flex gap-2"><span class="text-xcord-brand shrink-0">-</span> A Linux server or cloud account (any VPS provider works)</li>
          <li class="flex gap-2"><span class="text-xcord-brand shrink-0">-</span> A registered domain name with DNS access</li>
          <li class="flex gap-2"><span class="text-xcord-brand shrink-0">-</span> Docker installed on your local machine</li>
          <li class="flex gap-2"><span class="text-xcord-brand shrink-0">-</span> Git installed</li>
        </ul>
        <div class="mt-4 rounded-lg bg-xcord-landing-surface border border-xcord-landing-border p-4">
          <h3 class="text-sm font-semibold text-white mb-2">Minimum Server Requirements</h3>
          <div class="grid grid-cols-3 gap-4 text-sm">
            <div><span class="text-xcord-landing-text-muted">CPU</span><br />2 cores</div>
            <div><span class="text-xcord-landing-text-muted">RAM</span><br />4 GB</div>
            <div><span class="text-xcord-landing-text-muted">Disk</span><br />20 GB SSD</div>
          </div>
        </div>
      </section>

      {/* Step 1 */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">1. Clone the Repositories</h2>
        <pre class="rounded-lg bg-xcord-landing-surface border border-xcord-landing-border p-4 text-sm overflow-x-auto">
          <code>{`git clone https://github.com/xcord-net/xcord-fed.git
git clone https://github.com/xcord-net/xcord-topo.git`}</code>
        </pre>
        <p class="mt-3 text-sm text-xcord-landing-text-muted">
          <strong class="text-white">xcord-fed</strong> is your chat server.{' '}
          <strong class="text-white">xcord-topo</strong> is the topology designer you'll use to design and deploy your infrastructure.
        </p>
      </section>

      {/* Step 2 */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">2. Start the Topology Designer</h2>
        <pre class="rounded-lg bg-xcord-landing-surface border border-xcord-landing-border p-4 text-sm overflow-x-auto">
          <code>{`cd xcord-topo
./run.sh`}</code>
        </pre>
        <p class="mt-3 text-sm text-xcord-landing-text-muted">
          This builds and starts the topology designer at{' '}
          <code class="text-xcord-brand">http://localhost:8090</code>. For development mode with hot reload,
          use <code class="text-xcord-brand">./run.sh dev</code>.
        </p>
      </section>

      {/* Step 3 */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">3. Design Your Infrastructure</h2>
        <ol class="space-y-3 text-sm list-decimal list-inside">
          <li>Open <code class="text-xcord-brand">http://localhost:8090</code> and click <strong class="text-white">New Topology</strong></li>
          <li>Drag a <strong class="text-white">Host</strong> container from the catalog onto the canvas — this represents your server</li>
          <li>
            Add images inside the host by dragging from the catalog:
            <ul class="mt-2 ml-5 space-y-1 list-disc text-xcord-landing-text-muted">
              <li><strong class="text-white">FederationServer</strong> — the xcord instance</li>
              <li><strong class="text-white">PostgreSQL</strong> — database</li>
              <li><strong class="text-white">Redis</strong> — cache and message broker</li>
              <li><strong class="text-white">MinIO</strong> — file storage</li>
              <li><strong class="text-white">LiveKit</strong> — voice/video server</li>
              <li><strong class="text-white">Caddy</strong> — reverse proxy with automatic TLS</li>
            </ul>
          </li>
          <li>Draw <strong class="text-white">wires</strong> between services to establish connections (e.g., FederationServer → PostgreSQL)</li>
        </ol>
      </section>

      {/* Step 4 */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">4. Deploy with the Wizard</h2>
        <p class="text-sm mb-4">
          Click <strong class="text-white">Deploy</strong> in the toolbar to open the deploy wizard. It walks you through:
        </p>
        <div class="space-y-3">
          {[
            { step: 'Select Provider', desc: 'Choose your cloud provider. Linode and AWS are supported with native integration.' },
            { step: 'Configure', desc: 'Enter your API token, region, domain, and SSH key. The wizard shows help links for each credential.' },
            { step: 'Review', desc: 'Inspect the generated Terraform, resource summary, and monthly cost estimate.' },
            { step: 'Execute', desc: 'Click Deploy — Terraform runs init, plan, and apply with real-time streaming output.' },
          ].map((item) => (
            <div class="flex gap-3 text-sm">
              <span class="shrink-0 w-28 font-medium text-xcord-brand">{item.step}</span>
              <span class="text-xcord-landing-text-muted">{item.desc}</span>
            </div>
          ))}
        </div>
      </section>

      {/* Step 5 */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">5. Verify Your Deployment</h2>
        <pre class="rounded-lg bg-xcord-landing-surface border border-xcord-landing-border p-4 text-sm overflow-x-auto">
          <code>{`curl https://yourdomain.com/health
# Should return 200 OK`}</code>
        </pre>
        <p class="mt-3 text-sm text-xcord-landing-text-muted">
          Visit your domain in a browser, register the first account, and create your server.
        </p>
      </section>

      {/* Troubleshooting */}
      <section class="mb-12">
        <h2 class="text-2xl font-bold text-white mb-4">Troubleshooting</h2>
        <div class="rounded-lg bg-xcord-landing-surface border border-xcord-landing-border overflow-hidden text-sm">
          {[
            { problem: 'Site not loading', solution: 'Verify DNS A record points to your server IP' },
            { problem: '502 Bad Gateway', solution: 'Container may still be starting — check docker logs' },
          ].map((row, i) => (
            <div class={`flex gap-4 px-4 py-3 ${i > 0 ? 'border-t border-xcord-landing-border' : ''}`}>
              <span class="shrink-0 w-40 font-medium text-white">{row.problem}</span>
              <span class="text-xcord-landing-text-muted">{row.solution}</span>
            </div>
          ))}
        </div>
      </section>

      {/* Back link */}
      <div class="text-center">
        <A href="/pricing" class="text-sm text-xcord-brand hover:underline">
          Back to Pricing
        </A>
      </div>
    </article>
  );
}
