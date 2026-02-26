import { A } from "@solidjs/router";

export default function Footer() {
  return (
    <footer class="border-t border-xcord-landing-border bg-xcord-landing-bg">
      <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12">
        <div class="grid grid-cols-2 md:grid-cols-4 gap-8">
          <div>
            <h3 class="text-sm font-semibold text-white mb-3">Product</h3>
            <ul class="space-y-2">
              <li><A href="/pricing" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">Pricing</A></li>
              <li><A href="/register" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">Get Started</A></li>
            </ul>
          </div>
          <div>
            <h3 class="text-sm font-semibold text-white mb-3">Community</h3>
            <ul class="space-y-2">
              <li><a href="https://github.com/xcord-net" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors" target="_blank" rel="noopener noreferrer">GitHub</a></li>
            </ul>
          </div>
          <div>
            <h3 class="text-sm font-semibold text-white mb-3">Legal</h3>
            <ul class="space-y-2">
              <li><A href="/privacy" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">Privacy Policy</A></li>
              <li><A href="/terms" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">Terms of Service</A></li>
            </ul>
          </div>
          <div>
            <h3 class="text-sm font-semibold text-white mb-3">Xcord</h3>
            <p class="text-sm text-xcord-landing-text-muted">
              Community chat that scales with you.
            </p>
          </div>
        </div>
        <div class="mt-8 pt-8 border-t border-xcord-landing-border text-center text-sm text-xcord-landing-text-muted">
          &copy; {new Date().getFullYear()} xcord.net, LLC. All rights reserved.
        </div>
      </div>
    </footer>
  );
}
