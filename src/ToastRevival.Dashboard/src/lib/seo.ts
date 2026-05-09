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
    appendSiteName = true,
    ogType = 'website',
    jsonLd,
  } = options;

  useEffect(() => {
    const fullTitle = appendSiteName && !title.includes(SITE_NAME) ? `${title} - ${SITE_NAME}` : title;
    const url = `${SITE_URL}${path}`;

    document.title = fullTitle;

    ensureMeta('meta[name="description"]', 'name', 'description').setAttribute('content', description);
    ensureLink('canonical').setAttribute('href', url);

    ensureMeta('meta[property="og:title"]', 'property', 'og:title').setAttribute('content', fullTitle);
    ensureMeta('meta[property="og:description"]', 'property', 'og:description').setAttribute('content', description);
    ensureMeta('meta[property="og:url"]', 'property', 'og:url').setAttribute('content', url);
    ensureMeta('meta[property="og:type"]', 'property', 'og:type').setAttribute('content', ogType);
    ensureMeta('meta[property="og:site_name"]', 'property', 'og:site_name').setAttribute('content', SITE_NAME);
    ensureMeta('meta[property="og:image"]', 'property', 'og:image').setAttribute('content', image);

    ensureMeta('meta[name="twitter:card"]', 'name', 'twitter:card').setAttribute('content', 'summary_large_image');
    ensureMeta('meta[name="twitter:title"]', 'name', 'twitter:title').setAttribute('content', fullTitle);
    ensureMeta('meta[name="twitter:description"]', 'name', 'twitter:description').setAttribute('content', description);
    ensureMeta('meta[name="twitter:image"]', 'name', 'twitter:image').setAttribute('content', image);

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
  }, [title, description, path, image, appendSiteName, ogType, jsonLd]);
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
      'Branded Windows toast notifications for managed endpoints. Multi-tenant API, signed agent, admin dashboard. Built for MSPs and IT departments.',
    url: SITE_URL,
    offers: {
      '@type': 'Offer',
      name: 'Standard',
      price: '0.22',
      priceCurrency: 'USD',
      priceSpecification: {
        '@type': 'UnitPriceSpecification',
        price: '0.22',
        priceCurrency: 'USD',
        unitText: 'device per month',
        referenceQuantity: {
          '@type': 'QuantitativeValue',
          value: 1,
          unitCode: 'C62',
        },
      },
      eligibleQuantity: {
        '@type': 'QuantitativeValue',
        minValue: 100,
        unitText: 'device',
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
    name: `${SITE_NAME} - Standard plan`,
    description:
      'Single plan, $0.22 per device per month, 100-device subscription minimum, 14-day free trial. Includes every feature, no tiers.',
    brand: { '@type': 'Brand', name: SITE_NAME },
    offers: {
      '@type': 'AggregateOffer',
      priceCurrency: 'USD',
      lowPrice: '22.00',
      offerCount: 1,
      offers: [
        {
          '@type': 'Offer',
          name: 'Standard',
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
      ],
    },
  };
}

export function techArticleLd(opts: {
  headline: string;
  description: string;
  path: string;
}): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'TechArticle',
    headline: opts.headline,
    description: opts.description,
    inLanguage: 'en',
    url: `${SITE_URL}${opts.path}`,
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

export function breadcrumbLd(crumbs: Array<{ name: string; path: string }>): Record<string, unknown> {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: crumbs.map((c, i) => ({
      '@type': 'ListItem',
      position: i + 1,
      name: c.name,
      item: `${SITE_URL}${c.path}`,
    })),
  };
}
