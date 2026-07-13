import { createSignal, onMount, Show } from 'solid-js';
import styles from './Captcha.module.css';

interface CaptchaProps {
  onSolved: (captchaId: string, answer: string) => void;
  baseUrl?: string;
}

export default function Captcha(props: CaptchaProps) {
  const baseUrl = () => props.baseUrl ?? '/api/v1/auth/captcha';

  const [captchaId, setCaptchaId] = createSignal('');
  const [imageUrl, setImageUrl] = createSignal('');
  const [audioUrl, setAudioUrl] = createSignal('');
  const [answer, setAnswer] = createSignal('');
  const [loading, setLoading] = createSignal(true);
  const [disabled, setDisabled] = createSignal(false);
  const [useAudio, setUseAudio] = createSignal(false);

  const fetchChallenge = async () => {
    setLoading(true);
    setAnswer('');
    setUseAudio(false);
    try {
      const response = await fetch(baseUrl());
      if (response.ok) {
        const data = await response.json();
        setCaptchaId(data.captchaId);
        setImageUrl(data.imageUrl ?? '');
        setAudioUrl(data.audioUrl ?? '');
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
      <div class={styles.wrapper}>
        <label class={styles.label}>Security Check</label>
        <Show when={!loading()} fallback={<div class={styles.loading}>Loading challenge...</div>}>
          <div class={styles.challengeRow}>
            <Show
              when={!useAudio()}
              fallback={
                <audio
                  data-testid="captcha-audio"
                  class={styles.audio}
                  controls
                  src={audioUrl()}
                />
              }
            >
              <img
                data-testid="captcha-image"
                class={styles.image}
                src={imageUrl()}
                alt="Animated challenge: type the letters that appear as moving dots"
              />
            </Show>
            <div class={styles.actions}>
              <button
                type="button"
                data-testid="captcha-new"
                onClick={fetchChallenge}
                class={styles.linkButton}
                title="Get a new challenge"
              >
                New
              </button>
              <button
                type="button"
                data-testid="captcha-audio-toggle"
                onClick={() => setUseAudio(!useAudio())}
                class={styles.linkButton}
              >
                {useAudio() ? "Use image instead" : "Can't see it? Use audio"}
              </button>
            </div>
          </div>
          <input
            type="text"
            data-testid="captcha-input"
            value={answer()}
            onInput={(e) => handleInput(e.currentTarget.value)}
            class={styles.input}
            placeholder="Type the letters"
            autocomplete="off"
          />
        </Show>
      </div>
    </Show>
  );
}
