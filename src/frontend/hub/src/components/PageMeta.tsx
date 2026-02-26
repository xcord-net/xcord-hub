import { Title, Meta, Link } from '@solidjs/meta';

interface PageMetaProps {
  title: string;
  description: string;
  path: string;
  noindex?: boolean;
  ogImage?: string;
}

export default function PageMeta(props: PageMetaProps) {
  const origin = () => window.location.origin;
  const canonicalUrl = () => `${origin()}${props.path}`;
  const ogImageUrl = () => `${origin()}${props.ogImage ?? '/og-image.png'}`;

  return (
    <>
      <Title>{props.title}</Title>
      <Meta name="description" content={props.description} />
      <Link rel="canonical" href={canonicalUrl()} />

      {/* Open Graph */}
      <Meta property="og:title" content={props.title} />
      <Meta property="og:description" content={props.description} />
      <Meta property="og:url" content={canonicalUrl()} />
      <Meta property="og:image" content={ogImageUrl()} />
      <Meta property="og:type" content="website" />
      <Meta property="og:site_name" content="Xcord" />

      {/* Twitter Card */}
      <Meta name="twitter:card" content="summary_large_image" />
      <Meta name="twitter:title" content={props.title} />
      <Meta name="twitter:description" content={props.description} />
      <Meta name="twitter:image" content={ogImageUrl()} />

      {/* Robots */}
      {props.noindex && <Meta name="robots" content="noindex, nofollow" />}
    </>
  );
}
