import { A } from '@solidjs/router';
import PageMeta from '../components/PageMeta';

export default function PrivacyPolicy() {
  return (
    <article class="max-w-3xl mx-auto px-6 py-20 text-xcord-landing-text">
      <PageMeta
        title="Privacy Policy - Xcord Hub"
        description="How Xcord handles your data. No ads, no tracking, no data sales. AES-256 encryption at rest."
        path="/privacy"
      />
      <h1 class="text-4xl font-bold text-white mb-2">Privacy Policy</h1>
      <p class="text-xcord-landing-text-muted mb-12">Last updated: February 2026</p>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">1. What We Collect</h2>
        <p class="text-sm leading-relaxed mb-3">We collect only what is necessary to operate the service:</p>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li><strong class="text-white">Account data</strong> — email address, username, display name, hashed password</li>
          <li><strong class="text-white">Billing data</strong> — payment method details (processed by Stripe, never stored by us)</li>
          <li><strong class="text-white">Usage data</strong> — server logs, error reports, aggregate analytics</li>
          <li><strong class="text-white">Content</strong> — messages, files, and media you upload to your instance</li>
        </ul>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">2. How We Use Your Data</h2>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li>Operate and maintain the service</li>
          <li>Process payments and manage subscriptions</li>
          <li>Send transactional emails (account verification, password resets, billing)</li>
          <li>Diagnose and fix technical issues</li>
        </ul>
        <p class="text-sm leading-relaxed mt-3">
          We do <strong class="text-white">not</strong> sell your data, use it for advertising, or share it with
          third parties for marketing purposes.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">3. Encryption</h2>
        <p class="text-sm leading-relaxed">
          Data is encrypted at rest using AES-256-GCM with envelope encryption (KEK wrapping DEK). Each
          instance generates its own encryption keys on first boot — Xcord infrastructure operators do not
          have access to instance encryption keys. Communications between services use TLS.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">4. Data Location</h2>
        <p class="text-sm leading-relaxed">
          Your instance data is stored in the region you select during provisioning. Hub account data
          (email, username, billing) is stored in our primary infrastructure region. We do not transfer
          data across regions without your explicit action.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">5. Data Retention</h2>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li><strong class="text-white">Active accounts</strong> — data retained while your account is active</li>
          <li><strong class="text-white">Deleted accounts</strong> — data retained for 30 days, then permanently deleted</li>
          <li><strong class="text-white">Billing records</strong> — retained as required by tax law (typically 7 years)</li>
          <li><strong class="text-white">Server logs</strong> — rotated and deleted after 90 days</li>
        </ul>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">6. Your Rights</h2>
        <p class="text-sm leading-relaxed mb-3">You have the right to:</p>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li><strong class="text-white">Access</strong> — request a copy of your data</li>
          <li><strong class="text-white">Correction</strong> — update inaccurate information</li>
          <li><strong class="text-white">Deletion</strong> — delete your account and all associated data</li>
          <li><strong class="text-white">Export</strong> — download your data in a standard format</li>
        </ul>
        <p class="text-sm leading-relaxed mt-3">
          To exercise these rights, contact{' '}
          <a href="mailto:privacy@xcord.net" class="text-xcord-brand hover:underline">privacy@xcord.net</a>.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">7. Third-Party Services</h2>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li><strong class="text-white">Stripe</strong> — payment processing (subject to Stripe's privacy policy)</li>
          <li><strong class="text-white">Cloud providers</strong> — infrastructure hosting (Linode, AWS) for instance provisioning</li>
        </ul>
        <p class="text-sm leading-relaxed mt-3">
          We do not use analytics trackers, advertising networks, or social media pixels.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">8. Self-Hosted Instances</h2>
        <p class="text-sm leading-relaxed">
          If you self-host Xcord, you are the data controller. We have no access to data on self-hosted
          instances. This privacy policy applies only to Xcord's hosted platform and hub services.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">9. Changes to This Policy</h2>
        <p class="text-sm leading-relaxed">
          We will notify you of material changes via email at least 30 days before they take effect.
          The "last updated" date at the top reflects the most recent revision.
        </p>
      </section>

      <div class="text-center pt-4">
        <A href="/register" class="text-sm text-xcord-brand hover:underline">Back to Registration</A>
      </div>
    </article>
  );
}
