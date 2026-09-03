import { Show } from 'solid-js';
import ContactModal from '../components/ContactModal';
import PageMeta from '../components/PageMeta';
import ConfigStep from './GetStarted/ConfigStep';
import PaymentStep from './GetStarted/PaymentStep';
import AccountStep from './GetStarted/AccountStep';
import NotifyModal from './GetStarted/NotifyModal';
import { useGetStartedFlow } from './GetStarted/useGetStartedFlow';

export default function GetStarted() {
  const f = useGetStartedFlow();

  return (
    <>
      <PageMeta
        title="Get Started with Xcord - Launch Your Self-Hosted Chat Server"
        description="Create your own Xcord server in minutes. Choose a subdomain, pick a plan, and launch your self-hosted Discord alternative with voice and video streaming."
        path="/get-started"
      />
      <Show when={f.ready()} fallback={
        <div class="min-h-[60vh] flex items-center justify-center">
          <p class="text-xcord-text-muted">Loading...</p>
        </div>
      }>
        <div class="max-w-lg mx-auto py-12 px-4">
          {/* Step indicator */}
          <div data-testid="get-started-steps" class="flex items-center justify-center gap-2 mb-8">
            <div class={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold ${
              f.step() === 1 ? 'bg-xcord-brand text-xcord-landing-bg' : 'bg-xcord-bg-accent text-xcord-text-muted'
            }`}>1</div>
            <Show when={f.isPaidTier()}>
              <div class="w-8 h-0.5 bg-xcord-bg-accent" />
              <div class={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold ${
                f.step() === 2 ? 'bg-xcord-brand text-xcord-landing-bg' : 'bg-xcord-bg-accent text-xcord-text-muted'
              }`}>2</div>
            </Show>
            <Show when={!f.isLoggedIn()}>
              <div class="w-8 h-0.5 bg-xcord-bg-accent" />
              <div class={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold ${
                f.step() === f.accountStep() ? 'bg-xcord-brand text-xcord-landing-bg' : 'bg-xcord-bg-accent text-xcord-text-muted'
              }`}>{f.accountStep()}</div>
            </Show>
          </div>

          <Show when={f.step() === 1}>
            <ConfigStep
              totalSteps={f.totalSteps}
              loading={f.loading}
              error={f.error}
              isLoggedIn={f.isLoggedIn}
              paidTierAvailable={f.paidTierAvailable}
              isPaidTier={f.isPaidTier}
              subdomain={f.subdomain}
              subdomainStatus={f.subdomainStatus}
              subdomainError={f.subdomainError}
              subdomainValid={f.subdomainValid}
              onSubdomainInput={f.handleSubdomainInput}
              serverName={f.serverName}
              setServerName={f.setServerName}
              selectedTier={f.selectedTier}
              setSelectedTier={f.setSelectedTier}
              mediaEnabled={f.mediaEnabled}
              setMediaEnabled={f.setMediaEnabled}
              canProceedStep1={f.canProceedStep1}
              onNext={f.handleNext}
              onShowContact={() => f.setShowContact(true)}
              onNotifyOpen={f.handleNotifyOpen}
              setNotifyStatus={f.setNotifyStatus}
              setNotifyMessage={f.setNotifyMessage}
              setNotifyEmail={f.setNotifyEmail}
            />
          </Show>

          <Show when={f.step() === 2 && f.isPaidTier()}>
            <PaymentStep
              totalSteps={f.totalSteps}
              loading={f.loading}
              error={f.error}
              selectedTier={f.selectedTier}
              mediaEnabled={f.mediaEnabled}
              stripeCtx={f.stripeCtx}
              onBack={f.handleBackFromPayment}
              onConfirm={f.handleConfirmPayment}
            />
          </Show>

          <Show when={f.step() === f.accountStep() && !f.isLoggedIn()}>
            <AccountStep
              totalSteps={f.totalSteps}
              accountStep={f.accountStep}
              loading={f.loading}
              error={f.error}
              authError={() => f.auth.error}
              subdomain={f.subdomain}
              isPaidTier={f.isPaidTier}
              email={f.email}
              setEmail={f.setEmail}
              username={f.username}
              setUsername={f.setUsername}
              displayName={f.displayName}
              setDisplayName={f.setDisplayName}
              password={f.password}
              setPassword={f.setPassword}
              confirmPassword={f.confirmPassword}
              setConfirmPassword={f.setConfirmPassword}
              agreed={f.agreed}
              setAgreed={f.setAgreed}
              ageConfirmed={f.ageConfirmed}
              setAgeConfirmed={f.setAgeConfirmed}
              jurisdictionConfirmed={f.jurisdictionConfirmed}
              setJurisdictionConfirmed={f.setJurisdictionConfirmed}
              onCaptchaSolved={f.handleCaptchaSolved}
              onSubmit={f.handleSubmit}
              onBack={f.isPaidTier() ? () => { f.setStep(2); f.setError(''); } : f.handleBack}
            />
          </Show>
        </div>
      </Show>

      <NotifyModal
        notifyTier={f.notifyTier}
        setNotifyTier={f.setNotifyTier}
        notifyEmail={f.notifyEmail}
        setNotifyEmail={f.setNotifyEmail}
        notifyStatus={f.notifyStatus}
        notifyMessage={f.notifyMessage}
        onSubmit={f.handleNotify}
      />

      <ContactModal open={f.showContact()} onClose={() => f.setShowContact(false)} />
    </>
  );
}
