import { useEffect } from 'react';

const SITE_URL = 'https://toastnotification.com';
const SITE_NAME = 'Toast Notification';
const DEFAULT_OG_IMAGE = `${SITE_URL}/og-card.png`;

type JsonLd = Record<string, unknown> | Array<Record<string, unknown>>;

export interface SeoOptions {
  /** Page title; ≤ 60 chars recommended. Site suffix is appended automatically when `appendSiteName` is true. */
  title: string;
  /** Meta description; ≤ 160 chars recommended, MSP-vocabulary-dense. */
  description: string;
  /** Path beginning with `/` (e.g. `/pricing`). Used for canonical, og:url, twitter:url. */
  path: string;
  /** Optional OG/Twitter image absolute URL. Defaults to /og-card.png. */
  image?: string;
  /** Alt text for og:image and twitter:image. Defaults to the page title. */
  imageAlt?: string;
  /** Append " - Toast Notification" to the title. Defaults to true; pass false when the page already includes the brand. */
  appendSiteName?: boolean;
  /** og:type. Defaults to "website". */
  ogType?: string;
  /** Optional JSON-LD payload(s). One block per call; pass an array of schema objects to ship multiple types. */
  jsonLd?: JsonLd;
}

/** ID used to scope page-specific JSON-LD <script> tags so they can be cleaned up on unmount. */
const JSONLD_SCRIPT_ID = 'page-jsonld';

function ensureMeta(selector: string, attr: 'name' | 'property', key: string): HTMLMetaElement {
  let el = document.head.querySelector<HTMLMetaElement>(selector);
  if (!el) {
    el = document.createElement('meta');
    el.setAttribute(attr, key);
    document.head.appendChild(el);
  }
  return el;
}

function ensureLink(rel: string): HTMLLinkElement {
  let el = document.head.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`);
  if (!el) {
    el = document.createElement('link');
    el.setAttribute('rel', rel);
    document.head.appendChild(el);
  }
  return el;
}

function publicUrl(path: string): string {
  if (path === '/' || path.includes('.') || path.endsWith('/')) return `${SITE_URL}${path}`;
  return `${SITE_URL}${path}/`;
}

/**
 * Imperative head manager for marketing pages. No deps.
 *
 * Sets title, meta description, canonical, OG, Twitter card, and an
 * optional JSON-LD <script> block. Cleans up the page-scoped JSON-LD
 * tag on unmount so the next route doesn't inherit stale schema; the
 * meta tags are overwritten by whichever page mounts next, so they
 * don't need teardown.
 */
export function useSeo(options: SeoOptions) {
  const {
    title,
    description,
    path,
    image = DEFAULT_OG_IMAGE,
    imageAlt,
    appendSiteName = true,
    ogType = 'website',
    jsonLd,
  } = options;

  useEffect(() => {
    const fullTitle = appendSiteName && !title.includes(SITE_NAME) ? `${title} - ${SITE_NAME}` : title;
    const url = publicUrl(path);

    document.title = fullTitle;

    ensureMeta('meta[name="description"]', 'name', 'description').setAttribute('content', description);
    ensureLink('canonical').setAttribute('href', url);

    ensureMeta('meta[property="og:title"]', 'property', 'og:title').setAttribute('content', fullTitle);
    ensureMeta('meta[property="og:description"]', 'property', 'og:description').setAttribute('content', description);
    ensureMeta('meta[property="og:url"]', 'property', 'og:url').setAttribute('content', url);
    ensureMeta('meta[property="og:type"]', 'property', 'og:type').setAttribute('content', ogType);
    ensureMeta('meta[property="og:site_name"]', 'property', 'og:site_name').setAttribute('content', SITE_NAME);
    ensureMeta('meta[property="og:image"]', 'property', 'og:image').setAttribute('content', image);

    const resolvedImageAlt = imageAlt ?? fullTitle;
    ensureMeta('meta[property="og:image:alt"]', 'property', 'og:image:alt').setAttribute('content', resolvedImageAlt);

    ensureMeta('meta[name="twitter:card"]', 'name', 'twitter:card').setAttribute('content', 'summary_large_image');
    ensureMeta('meta[name="twitter:title"]', 'name', 'twitter:title').setAttribute('content', fullTitle);
    ensureMeta('meta[name="twitter:description"]', 'name', 'twitter:description').setAttribute('content', description);
    ensureMeta('meta[name="twitter:image"]', 'name', 'twitter:image').setAttribute('content', image);
    ensureMeta('meta[name="twitter:image:alt"]', 'name', 'twitter:image:alt').setAttribute('content', resolvedImageAlt);
    ensureMeta('meta[name="twitter:url"]', 'name', 'twitter:url').setAttribute('content', url);
    ensureMeta('meta[name="twitter:site"]', 'name', 'twitter:site').setAttribute('content', '@Toast2IT');
    ensureMeta('meta[name="twitter:creator"]', 'name', 'twitter:creator').setAttribute('content', '@Toast2IT');

    let script: HTMLScriptElement | null = null;
    if (jsonLd) {
      script = document.createElement('script');
      script.type = 'application/ld+json';
      script.id = JSONLD_SCRIPT_ID;
      // Defensive: escape any literal "</" that could close the <script> tag if a
      // future schema field ever holds user-controllable text. JSON parsers
      // accept the backslash-escaped form unchanged.
      script.text = JSON.stringify(jsonLd).replace(/<\//g, '<\\/');
      document.head.appendChild(script);
    }

    return () => {
      const existing = document.getElementById(JSONLD_SCRIPT_ID);
      if (existing) existing.remove();
    };
  }, [title, description, path, image, imageAlt, appendSiteName, ogType, jsonLd]);
}

/** Helpers for common JSON-LD shapes. */

export function softwareApplicationLd(): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: SITE_NAME,
    applicationCategory: 'BusinessApplication',
    applicationSubCategory: 'EndpointManagement',
    operatingSystem: 'Windows 10, Windows 11',
    description:
      'Branded, signed, trackable Windows toast notifications for managed endpoints, plus dashboard-managed desktop info overlays and device lock screen branding. Multi-tenant API, signed agent, admin dashboard. Built for MSPs and IT departments.',
    url: SITE_URL,
    featureList: [
      'Branded Windows toast notifications with templates, scenarios, hero images, logos, action buttons, and custom audio',
      'Device, group, and tenant-wide targeting with scheduled sends',
      'Delivery and interaction tracking with CSV and PDF export',
      'Desktop info overlay — a dashboard-managed BgInfo replacement',
      'Per-device lock screen branding',
      'Per-tenant HMAC-signed payloads and DPAPI-protected agent configuration',
      'Content moderation and tenant blocklists',
      'Microsoft Entra SSO, TOTP multi-factor authentication, and role-based access',
      'Deployment by signed MSI, Intune, Microsoft Store, or RMM silent install',
    ],
    offers: {
      '@type': 'Offer',
      name: 'Managed SaaS',
      description: 'First 25 active devices free, then $0.22 per device per month, with no device cap.',
      price: '0.22',
      priceCurrency: 'USD',
      priceSpecification: {
        '@type': 'UnitPriceSpecification',
        price: '0.22',
        priceCurrency: 'USD',
        unitText: 'device per month',
      },
      availability: 'https://schema.org/InStock',
    },
    publisher: {
      '@type': 'Organization',
      name: 'Toast2IT, LLC',
      url: SITE_URL,
    },
  };
}

export function pricingProductLd(): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'Product',
    name: `${SITE_NAME} - Managed SaaS plan`,
    description:
      'Three tiers: Free Trial (2 devices, 14 days, reviewed), Managed SaaS (first 25 devices free, then $0.22 per device per month, no device cap), or Roll Your Own (Docker Compose self-host, free, no device cap). Every tier ships every feature.',
    brand: { '@type': 'Brand', name: SITE_NAME },
    offers: {
      '@type': 'AggregateOffer',
      priceCurrency: 'USD',
      lowPrice: '0.00',
      highPrice: '0.22',
      offerCount: 3,
      offers: [
        {
          '@type': 'Offer',
          name: 'Free Trial',
          price: '0.00',
          priceCurrency: 'USD',
          availability: 'https://schema.org/InStock',
        },
        {
          '@type': 'Offer',
          name: 'Managed SaaS',
          description: 'First 25 active devices free, then $0.22 per device per month, no device cap.',
          price: '0.22',
          priceCurrency: 'USD',
          priceSpecification: {
            '@type': 'UnitPriceSpecification',
            price: '0.22',
            priceCurrency: 'USD',
            unitText: 'device per month',
          },
          availability: 'https://schema.org/InStock',
        },
        {
          '@type': 'Offer',
          name: 'Roll Your Own (self-hosted)',
          price: '0.00',
          priceCurrency: 'USD',
          availability: 'https://schema.org/InStock',
        },
      ],
    },
  };
}

// REVIEW-2026-06-06 SEO-L8 REJECTED-by-design: machine-readable OpenAPI spec requires authoring the full specification; linked to REST-M1 and DEVOPS-H4 CI pipeline milestone for automatic generation
export function techArticleLd(opts: {
  headline: string;
  description: string;
  path: string;
  datePublished?: string;
  dateModified?: string;
}): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'TechArticle',
    headline: opts.headline,
    description: opts.description,
    inLanguage: 'en',
    url: publicUrl(opts.path),
    ...(opts.datePublished && { datePublished: opts.datePublished }),
    ...(opts.dateModified || opts.datePublished
      ? { dateModified: opts.dateModified ?? opts.datePublished }
      : {}),
    isPartOf: {
      '@type': 'WebSite',
      name: SITE_NAME,
      url: SITE_URL,
    },
    publisher: {
      '@type': 'Organization',
      name: 'Toast2IT, LLC',
      url: SITE_URL,
    },
  };
}

export function websiteLd(): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: SITE_NAME,
    alternateName: 'Toast2IT',
    url: SITE_URL,
  };
}

export function organizationLd(): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: 'Toast2IT LLC',
    url: SITE_URL,
    logo: { '@type': 'ImageObject', url: `${SITE_URL}/logo.png` },
    sameAs: ['https://github.com/keithrlucier/toast-notification'],
  };
}

export function faqLd(items: Array<{ q: string; a: string }>): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: items.map(item => ({
      '@type': 'Question',
      name: item.q,
      acceptedAnswer: {
        '@type': 'Answer',
        text: item.a,
      },
    })),
  };
}

export function breadcrumbLd(crumbs: Array<{ name: string; path: string }>): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: crumbs.map((c, i) => ({
      '@type': 'ListItem',
      position: i + 1,
      name: c.name,
      item: publicUrl(c.path),
    })),
  };
}
