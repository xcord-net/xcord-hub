export interface FeatureCardProps {
  icon: string;
  title: string;
  description: string;
}

export default function FeatureCard(props: FeatureCardProps) {
  return (
    <div class="bg-xcord-landing-surface border border-xcord-landing-border rounded-xl p-6 hover:border-xcord-brand/30 transition-colors">
      <div class="text-3xl mb-4">{props.icon}</div>
      <h3 class="text-lg font-semibold text-white mb-2">{props.title}</h3>
      <p class="text-sm text-xcord-landing-text-muted leading-relaxed">{props.description}</p>
    </div>
  );
}
