import { createSignal, onMount, Show } from 'solid-js';

interface CaptchaProps {
  onSolved: (captchaId: string, answer: string) => void;
}

export default function Captcha(props: CaptchaProps) {
  const [captchaId, setCaptchaId] = createSignal('');
  const [question, setQuestion] = createSignal('');
  const [answer, setAnswer] = createSignal('');
  const [loading, setLoading] = createSignal(true);
  const [disabled, setDisabled] = createSignal(false);

  const fetchChallenge = async () => {
    setLoading(true);
    setAnswer('');
    try {
      const response = await fetch('/api/v1/auth/captcha');
      if (response.ok) {
        const data = await response.json();
        setCaptchaId(data.captchaId);
        setQuestion(data.question);
        if (data.captchaId === 'disabled') {
          setDisabled(true);
          props.onSolved('disabled', '');
        }
      }
    } catch {
      // Ignore fetch errors
    } finally {
      setLoading(false);
    }
  };

  onMount(fetchChallenge);

  const handleInput = (value: string) => {
    setAnswer(value);
    props.onSolved(captchaId(), value);
  };

  return (
    <Show when={!disabled()}>
      <div>
        <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">
          Security Check
        </label>
        <Show when={!loading()} fallback={<div class="text-xs text-xcord-text-muted">Loading challenge...</div>}>
          <div class="flex items-center gap-2">
            <span class="text-sm text-xcord-text-secondary font-mono">
              What is {question()}?
            </span>
            <button
              type="button"
              onClick={fetchChallenge}
              class="text-xs text-xcord-text-link hover:underline"
              title="Get a new challenge"
            >
              New
            </button>
          </div>
          <input
            type="text"
            value={answer()}
            onInput={(e) => handleInput(e.currentTarget.value)}
            class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand mt-1"
            placeholder="Your answer"
            autocomplete="off"
          />
        </Show>
      </div>
    </Show>
  );
}
