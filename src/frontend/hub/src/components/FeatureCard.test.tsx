import { describe, it, expect } from 'vitest';
import { createSignal } from 'solid-js';
import { render } from '@solidjs/testing-library';
import FeatureCard from './FeatureCard';

describe('FeatureCard', () => {
  const baseProps = {
    icon: 'X',
    title: 'Title',
    description: 'Body text.',
  };

  it('renders without crashing', () => {
    const { container } = render(() => <FeatureCard {...baseProps} />);
    expect(container.firstElementChild).toBeInTheDocument();
  });

  it('renders the title text in a heading', () => {
    const { getByText } = render(() => <FeatureCard {...baseProps} title="Encryption" />);
    const h3 = getByText('Encryption');
    expect(h3.tagName).toBe('H3');
  });

  it('renders the description text', () => {
    const { getByText } = render(() => (
      <FeatureCard {...baseProps} description="Your data, your keys." />
    ));
    expect(getByText('Your data, your keys.')).toBeInTheDocument();
  });

  it('renders the icon string', () => {
    const { getByText } = render(() => <FeatureCard {...baseProps} icon="ICN" />);
    expect(getByText('ICN')).toBeInTheDocument();
  });

  it('updates rendered text when props change', () => {
    const [title, setTitle] = createSignal('First');
    const { getByText } = render(() => (
      <FeatureCard icon="A" title={title()} description="d" />
    ));
    expect(getByText('First')).toBeInTheDocument();
    setTitle('Second');
    expect(getByText('Second')).toBeInTheDocument();
  });
});
