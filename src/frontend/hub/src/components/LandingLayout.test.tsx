import { describe, it, expect } from 'vitest';
import LandingLayout from './LandingLayout';
import { renderWithRouter } from '../tests/helpers/renderWithRouter';

describe('LandingLayout', () => {
  it('renders without crashing', () => {
    const { container } = renderWithRouter(() => (
      <LandingLayout><span>child</span></LandingLayout>
    ));
    expect(container.querySelector('header')).toBeInTheDocument();
    expect(container.querySelector('footer')).toBeInTheDocument();
  });

  it('renders children inside the main slot', () => {
    const { getByText } = renderWithRouter(() => (
      <LandingLayout><span data-testid="child">hello-world</span></LandingLayout>
    ));
    expect(getByText('hello-world')).toBeInTheDocument();
  });

  it('renders the brand logo link', () => {
    const { getByTestId } = renderWithRouter(() => (
      <LandingLayout><div /></LandingLayout>
    ));
    expect(getByTestId('landing-logo')).toBeInTheDocument();
  });

  it('renders desktop nav links', () => {
    const { getByTestId } = renderWithRouter(() => (
      <LandingLayout><div /></LandingLayout>
    ));
    expect(getByTestId('nav-pricing')).toBeInTheDocument();
    expect(getByTestId('nav-download')).toBeInTheDocument();
    expect(getByTestId('nav-login')).toBeInTheDocument();
    expect(getByTestId('nav-signup')).toBeInTheDocument();
  });

  it('renders the GitHub external link with target=_blank', () => {
    const { getByTestId } = renderWithRouter(() => (
      <LandingLayout><div /></LandingLayout>
    ));
    const gh = getByTestId('nav-github') as HTMLAnchorElement;
    expect(gh.target).toBe('_blank');
    expect(gh.rel).toContain('noopener');
  });
});
