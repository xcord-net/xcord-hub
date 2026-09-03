import { type ParentProps } from "solid-js";
import { A } from "@solidjs/router";
import Footer from "./Footer";
import Logo from "./Logo";

const NAV_LINK =
  "text-sm text-xcord-landing-text-muted hover:text-xcord-landing-text transition-colors duration-200 rounded-sm focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-xcord-brand";

const SIGNUP =
  "px-4 py-2 bg-xcord-brand text-xcord-landing-bg text-sm font-semibold rounded-md hover:bg-xcord-brand-hover transition-colors duration-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-xcord-brand";

export default function LandingLayout(props: ParentProps) {
  return (
    <div class="min-h-screen bg-xcord-landing-bg text-xcord-landing-text flex flex-col">
      <header class="border-b border-xcord-landing-border">
        <nav class="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <A
            data-testid="landing-logo"
            href="/"
            class="font-display text-xl font-extrabold tracking-[-0.01em] text-xcord-landing-text rounded-sm focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-xcord-brand"
          >
            <Logo />
          </A>
          <div class="hidden sm:flex items-center gap-6">
            <A data-testid="nav-pricing" href="/pricing" class={NAV_LINK}>
              Pricing
            </A>
            <A data-testid="nav-download" href="/download" class={NAV_LINK}>
              Download
            </A>
            <a
              data-testid="nav-github"
              href="https://github.com/xcord-net"
              target="_blank"
              rel="noopener noreferrer"
              class={NAV_LINK}
            >
              GitHub
            </a>
            <A data-testid="nav-login" href="/login" class={NAV_LINK}>
              Log in
            </A>
            <A data-testid="nav-signup" href="/register" class={SIGNUP}>
              Sign up
            </A>
          </div>
          {/* Mobile menu button */}
          <div class="sm:hidden">
            <A href="/register" class={SIGNUP}>
              Sign up
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
