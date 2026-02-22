import { type ParentProps } from "solid-js";
import { A } from "@solidjs/router";
import Footer from "./Footer";

export default function LandingLayout(props: ParentProps) {
  return (
    <div class="min-h-screen bg-xcord-landing-bg text-xcord-landing-text flex flex-col">
      <header class="border-b border-xcord-landing-border">
        <nav class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <A data-testid="landing-logo" href="/" class="text-xl font-bold text-white">
            Xcord
          </A>
          <div class="hidden sm:flex items-center gap-6">
            <A data-testid="nav-pricing" href="/pricing" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">
              Pricing
            </A>
            <A data-testid="nav-login" href="/login" class="text-sm text-xcord-landing-text-muted hover:text-white transition-colors">
              Log in
            </A>
            <A
              data-testid="nav-signup"
              href="/register"
              class="px-4 py-2 bg-xcord-brand text-white text-sm font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors"
            >
              Sign Up
            </A>
          </div>
          {/* Mobile menu button */}
          <div class="sm:hidden">
            <A
              href="/register"
              class="px-4 py-2 bg-xcord-brand text-white text-sm font-medium rounded-lg hover:bg-xcord-brand-hover transition-colors"
            >
              Sign Up
            </A>
          </div>
        </nav>
      </header>
      <main class="flex-1">
        {props.children}
      </main>
      <Footer />
    </div>
  );
}
