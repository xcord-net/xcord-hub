import { A } from '@solidjs/router';

export default function TermsOfService() {
  return (
    <article class="max-w-3xl mx-auto px-6 py-20 text-xcord-landing-text">
      <h1 class="text-4xl font-bold text-white mb-2">Terms of Service</h1>
      <p class="text-xcord-landing-text-muted mb-12">Last updated: February 2026</p>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">1. Acceptance of Terms</h2>
        <p class="text-sm leading-relaxed">
          By creating an account or using any Xcord hosted service, you agree to these Terms of Service.
          If you do not agree, do not use the service. These terms apply to Xcord's hosted platform only —
          self-hosted instances are governed by the Apache 2.0 license.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">2. Eligibility</h2>
        <p class="text-sm leading-relaxed">
          You must be at least 18 years old to create an account. By registering, you confirm that you meet
          this age requirement. Accounts created by minors will be terminated.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">3. Your Account</h2>
        <p class="text-sm leading-relaxed">
          You are responsible for maintaining the security of your account credentials. You are responsible
          for all activity under your account. Notify us immediately if you suspect unauthorized access.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">4. Encryption</h2>
        <p class="text-sm leading-relaxed">
          Xcord uses end-to-end encryption for certain communications and encrypts data at rest. By using
          the service, you acknowledge that encryption technology may be subject to legal restrictions in
          your jurisdiction. It is your responsibility to ensure that your use of encrypted communications
          complies with all applicable local, national, and international laws.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">5. Acceptable Use</h2>
        <p class="text-sm leading-relaxed mb-3">You agree not to use the service to:</p>
        <ul class="text-sm space-y-1.5 list-disc list-inside text-xcord-landing-text-muted">
          <li>Violate any applicable law or regulation</li>
          <li>Distribute malware, spam, or phishing content</li>
          <li>Harass, threaten, or abuse other users</li>
          <li>Distribute content that exploits minors</li>
          <li>Attempt to gain unauthorized access to other accounts or systems</li>
          <li>Interfere with or disrupt the service infrastructure</li>
        </ul>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">6. Content</h2>
        <p class="text-sm leading-relaxed">
          You retain ownership of content you create. By uploading content, you grant Xcord a limited license
          to store, transmit, and display it as necessary to operate the service. We do not claim ownership
          of your content and do not use it for advertising or training purposes.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">7. Service Availability</h2>
        <p class="text-sm leading-relaxed">
          We aim for high availability but do not guarantee uninterrupted service. Scheduled maintenance
          windows will be announced in advance when possible. The service is provided "as is" without
          warranty of any kind.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">8. Termination</h2>
        <p class="text-sm leading-relaxed">
          You may delete your account at any time. We may suspend or terminate accounts that violate these
          terms. Upon termination, your data will be retained for 30 days before permanent deletion, during
          which you may request an export.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">9. Limitation of Liability</h2>
        <p class="text-sm leading-relaxed">
          To the maximum extent permitted by law, Xcord shall not be liable for any indirect, incidental,
          special, or consequential damages arising from your use of the service.
        </p>
      </section>

      <section class="mb-10">
        <h2 class="text-xl font-bold text-white mb-3">10. Changes to Terms</h2>
        <p class="text-sm leading-relaxed">
          We may update these terms from time to time. Material changes will be communicated via email or
          in-app notification at least 30 days before taking effect. Continued use of the service after
          changes take effect constitutes acceptance.
        </p>
      </section>

      <div class="text-center pt-4">
        <A href="/register" class="text-sm text-xcord-brand hover:underline">Back to Registration</A>
      </div>
    </article>
  );
}
