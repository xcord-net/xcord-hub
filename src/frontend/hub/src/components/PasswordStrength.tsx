interface PasswordStrengthProps {
  password: string;
}

export default function PasswordStrength(props: PasswordStrengthProps) {
  const checks = () => {
    const p = props.password;
    return [
      p.length >= 8,
      /[A-Z]/.test(p),
      /[a-z]/.test(p),
      /[0-9]/.test(p),
      /[^A-Za-z0-9]/.test(p),
      p.length >= 12,
    ];
  };

  const score = () => checks().filter(Boolean).length;

  const level = () => {
    const s = score();
    if (s <= 2) return { label: 'Weak', color: 'bg-xcord-red' };
    if (s <= 3) return { label: 'Fair', color: 'bg-xcord-yellow' };
    if (s <= 4) return { label: 'Good', color: 'bg-xcord-brand' };
    return { label: 'Strong', color: 'bg-xcord-green' };
  };

  return (
    <div class="mt-2">
      <div class="flex gap-1 mb-1">
        {[0, 1, 2, 3].map((i) => (
          <div class={`h-1 flex-1 rounded-full transition ${i < Math.ceil(score() / 1.5) ? level().color : 'bg-xcord-bg-accent'}`} />
        ))}
      </div>
      <span class="text-xs text-xcord-text-muted">{props.password.length > 0 ? level().label : ''}</span>
    </div>
  );
}
