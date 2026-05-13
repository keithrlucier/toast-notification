import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const SITE_URL = 'https://toastnotification.com';
const SITE_NAME = 'Toast Notification';
const UPDATED = '2026-05-12';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const dist = join(root, 'dist');
const shellPath = join(dist, 'index.html');

function publicUrl(path) {
  if (path === '/' || path.includes('.') || path.endsWith('/')) return `${SITE_URL}${path}`;
  return `${SITE_URL}${path}/`;
}

const softwareApplicationLd = () => ({
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: SITE_NAME,
  applicationCategory: 'BusinessApplication',
  applicationSubCategory: 'EndpointManagement',
  operatingSystem: 'Windows 10, Windows 11',
  description:
    'Managed Windows notification platform for MSPs and IT departments. Sends branded, signed, trackable toast notifications to enrolled endpoints.',
  url: SITE_URL,
  offers: {
    '@type': 'Offer',
    name: 'Standard',
    price: '22.00',
    priceCurrency: 'USD',
    priceSpecification: {
      '@type': 'PriceSpecification',
      price: '22.00',
      priceCurrency: 'USD',
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
});

const productLd = () => ({
  '@context': 'https://schema.org',
  '@type': 'Product',
  name: `${SITE_NAME} Standard plan`,
  description:
    'Managed Windows notification platform with reviewed trial access, fleet block pricing, signed delivery, and audit reporting.',
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
          price: '22.00',
          priceCurrency: 'USD',
          priceSpecification: {
            '@type': 'PriceSpecification',
            price: '22.00',
            priceCurrency: 'USD',
          },
        availability: 'https://schema.org/InStock',
      },
    ],
  },
});

const articleLd = (route) => ({
  '@context': 'https://schema.org',
  '@type': 'TechArticle',
  headline: route.title,
  description: route.description,
  inLanguage: 'en',
  dateModified: UPDATED,
  url: publicUrl(route.path),
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
});

const breadcrumbLd = (items) => ({
  '@context': 'https://schema.org',
  '@type': 'BreadcrumbList',
  itemListElement: items.map((item, index) => ({
    '@type': 'ListItem',
    position: index + 1,
    name: item.name,
    item: publicUrl(item.path),
  })),
});

const routes = [
  {
    path: '/',
    title: 'Managed Windows notifications for MSPs',
    description:
      'Toast Notification helps MSPs send branded, signed, trackable Windows toast notifications with audit evidence and deployment through MSI, Intune, Store, or RMM.',
    priority: '1.0',
    changefreq: 'weekly',
    jsonLd: [softwareApplicationLd(), breadcrumbLd([{ name: 'Home', path: '/' }])],
    body: `
      <h1>Managed Windows notifications for MSPs</h1>
      <p>Toast Notification is a SaaS platform for sending branded, trackable Windows toast notifications to managed endpoints. MSPs and IT teams use it when scripts, msg.exe, email, or RMM-bundled alert widgets are not enough.</p>
      <h2>What it does</h2>
      <ul>
        <li>Send rich Windows toast notifications with branding, templates, action buttons, hero images, logos, and audio.</li>
        <li>Target one device, a device group, or every endpoint in a tenant.</li>
        <li>Track delivered, clicked, dismissed, and failed outcomes.</li>
        <li>Export tenant audit evidence to CSV or PDF.</li>
        <li>Deploy through MSI, Intune, Microsoft Store, or RMM silent install.</li>
      </ul>
      <p>Trial access is reviewed before activation. Paid fleet blocks start at $22 per month for 26-100 devices.</p>
    `,
  },
  {
    path: '/pricing',
    title: 'Pricing',
    description:
      'Toast Notification pricing: reviewed trial access, then $22/month for 26-100 devices and $44/month for 101-200 devices.',
    priority: '0.9',
    changefreq: 'weekly',
    jsonLd: [
      productLd(),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Pricing', path: '/pricing' },
      ]),
    ],
    body: `
      <h1>Toast Notification pricing</h1>
      <p>Toast Notification has reviewed trial access before tenant activation. Paid fleet blocks are predictable: $22/month for 26-100 devices and $44/month for 101-200 devices.</p>
      <ul>
        <li>Trial access: reviewed before activation.</li>
        <li>26-100 devices: $22 per month.</li>
        <li>300 devices: $66 per month.</li>
        <li>1,000 devices: $220 per month.</li>
        <li>5,000 devices: $1,100 per month.</li>
      </ul>
      <p>All current features are included: signed delivery, audit reporting, deployment guides, templates, targeting, and delivery tracking.</p>
    `,
  },
  {
    path: '/security',
    title: 'Security architecture',
    description:
      'Toast Notification security architecture: HTTPS transport, HMAC-SHA256 payload signing, tenant isolation, MFA controls, audit logging, and responsible disclosure.',
    priority: '0.85',
    changefreq: 'monthly',
    jsonLd: [
      articleLd({
        path: '/security',
        title: 'Toast Notification security architecture',
        description:
          'Security controls for managed Windows notifications: HMAC payload signing, tenant isolation, MFA elevation, and audit logging.',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Security', path: '/security' },
      ]),
    ],
    body: `
      <h1>Security controls for managed Windows notifications</h1>
      <p>Toast Notification is designed for MSPs that need tenant isolation, signed endpoint delivery, audit evidence, and clear operational boundaries.</p>
      <ul>
        <li>Notification payloads are signed per tenant with HMAC-SHA256 and verified by the Windows agent before render.</li>
        <li>Tenant-facing API queries are scoped by tenant ID.</li>
        <li>Broadcast-to-all sends require MFA elevation.</li>
        <li>Endpoint configuration is protected with Windows DPAPI.</li>
        <li>Audit records track sends, deliveries, user actions, device registrations, and tenant changes.</li>
      </ul>
      <p>For coordinated disclosure or security documentation, contact security@toastnotification.com.</p>
    `,
  },
  {
    path: '/docs',
    title: 'Documentation',
    description:
      'Toast Notification documentation for MSPs: getting started, Store deployment, Intune deployment, RMM silent install, API reference, security posture, and operational notes.',
    priority: '0.8',
    changefreq: 'weekly',
    jsonLd: [
      articleLd({
        path: '/docs',
        title: 'Toast Notification documentation',
        description:
          'Documentation hub for deploying and operating Toast Notification across managed Windows endpoints.',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
      ]),
    ],
    body: `
      <h1>Toast Notification documentation</h1>
      <p>Documentation for MSPs and IT departments deploying Toast Notification across managed Windows endpoints.</p>
      <ul>
        <li><a href="/docs/getting-started">Getting started</a>: create a tenant, install an agent, and send the first notification.</li>
        <li><a href="/docs/deploy/store">Microsoft Store deployment</a>: install and register the Windows agent from Store distribution.</li>
        <li><a href="/docs/deploy/intune">Intune deployment</a>: deploy the agent to managed corporate endpoints.</li>
        <li><a href="/docs/deploy/rmm">RMM silent install</a>: deploy through RMM tooling with MSI properties.</li>
        <li><a href="/docs/api">API reference</a>: authentication, notifications, devices, templates, and audit endpoints.</li>
      </ul>
    `,
  },
  {
    path: '/docs/getting-started',
    title: 'Getting started',
    description:
      'Create a Toast Notification tenant, install the Windows agent, register the endpoint, and send a branded test notification.',
    priority: '0.7',
    changefreq: 'monthly',
    body: `
      <h1>Getting started with Toast Notification</h1>
      <p>Create a tenant, verify the administrator account, install the Windows agent, register the endpoint, and send the first branded notification from the dashboard.</p>
      <ol>
        <li>Request trial access and set your password after approval.</li>
        <li>Install the Windows agent.</li>
        <li>Confirm the device appears in the dashboard.</li>
        <li>Send a notification using one of the included templates.</li>
        <li>Review delivery and interaction events in history.</li>
      </ol>
    `,
  },
  {
    path: '/docs/deploy/store',
    title: 'Microsoft Store deployment',
    description:
      'Deploy Toast Notification through the Microsoft Store and register endpoints with tenant configuration.',
    priority: '0.7',
    changefreq: 'monthly',
    body: `
      <h1>Microsoft Store deployment</h1>
      <p>The Store deployment path is for individual users and BYOD-style Windows endpoints. Install the agent, provide tenant configuration, and verify the endpoint appears in Toast Notification.</p>
      <p>Use this path when endpoints can install from the Microsoft Store and do not require central MSI deployment.</p>
    `,
  },
  {
    path: '/docs/deploy/intune',
    title: 'Intune deployment',
    description:
      'Deploy the Toast Notification Windows agent to managed endpoints with Microsoft Intune.',
    priority: '0.7',
    changefreq: 'monthly',
    body: `
      <h1>Intune deployment</h1>
      <p>Intune deployment lets IT teams push the Windows agent to managed endpoints and set tenant registration values centrally.</p>
      <p>This path is suited for corporate Windows fleets managed through Microsoft Endpoint Manager.</p>
    `,
  },
  {
    path: '/docs/deploy/rmm',
    title: 'RMM silent install',
    description:
      'Deploy Toast Notification with RMM tools using silent MSI install properties for tenant ID and server URL.',
    priority: '0.7',
    changefreq: 'monthly',
    body: `
      <h1>RMM silent install</h1>
      <p>MSPs can deploy Toast Notification through RMM tools that support silent MSI installation. The MSI accepts tenant ID and server URL properties so endpoints enroll into the correct tenant.</p>
      <p>This deployment path is compatible with common RMM platforms that can execute msiexec with command-line properties.</p>
    `,
  },
  {
    path: '/docs/api',
    title: 'API reference',
    description:
      'Toast Notification API reference for authentication, devices, templates, notifications, delivery status, and audit export.',
    priority: '0.7',
    changefreq: 'monthly',
    body: `
      <h1>Toast Notification API reference</h1>
      <p>The Toast Notification API supports tenant administration, device registration, notification send workflows, delivery reporting, templates, assets, moderation, and audit exports.</p>
      <p>Authenticated API calls use JWTs. Device endpoints use tenant-scoped device tokens.</p>
    `,
  },
  {
    path: '/llms',
    title: 'LLM product brief',
    description:
      'Canonical product facts about Toast Notification for AI assistants and search crawlers: audience, pricing, deployment, security, and documentation links.',
    priority: '0.6',
    changefreq: 'monthly',
    jsonLd: [
      articleLd({
        path: '/llms',
        title: 'Toast Notification LLM product brief',
        description:
          'Canonical product facts for AI assistants and search crawlers.',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'LLM product brief', path: '/llms' },
      ]),
    ],
    body: `
      <h1>Toast Notification LLM product brief</h1>
      <p>Toast Notification is a managed Windows notification platform for MSPs and IT departments. It sends branded, signed, trackable Windows toast notifications to enrolled endpoints.</p>
      <h2>Canonical facts</h2>
      <ul>
        <li>Primary audience: MSPs and IT departments.</li>
        <li>Pricing: reviewed trial access, then $22/month for 26-100 devices and $44/month for 101-200 devices.</li>
        <li>Deployment: MSI, Intune, Microsoft Store, or RMM silent install.</li>
        <li>Security: HMAC payload signing, tenant isolation, MFA-gated broadcast sends, and audit records.</li>
        <li>Plain-text crawler file: <a href="/llms.txt">/llms.txt</a>.</li>
      </ul>
    `,
  },
];

for (const route of routes) {
  route.jsonLd ??= [
    articleLd(route),
    breadcrumbLd([
      { name: 'Home', path: '/' },
      { name: route.title, path: route.path },
    ]),
  ];
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function pageTitle(route) {
  return route.title.includes(SITE_NAME) ? route.title : `${route.title} - ${SITE_NAME}`;
}

function setTitle(html, title) {
  return html.replace(/<title>.*?<\/title>/s, `<title>${escapeHtml(title)}</title>`);
}

function setMeta(html, selector, attr, key, content) {
  const escaped = escapeHtml(content);
  const re = new RegExp(`<meta\\s+${attr}="${key}"\\s+content="[^"]*"\\s*/?>`, 'i');
  const tag = `<meta ${attr}="${key}" content="${escaped}" />`;
  if (re.test(html)) return html.replace(re, tag);
  return html.replace('</head>', `    ${tag}\n  </head>`);
}

function setLink(html, rel, attrs) {
  const attrText = Object.entries(attrs)
    .map(([key, value]) => `${key}="${escapeHtml(value)}"`)
    .join(' ');
  const re = new RegExp(`<link\\s+rel="${rel}"[^>]*>`, 'i');
  const tag = `<link rel="${rel}" ${attrText} />`;
  if (re.test(html)) return html.replace(re, tag);
  return html.replace('</head>', `    ${tag}\n  </head>`);
}

function jsonLdScript(jsonLd) {
  const json = JSON.stringify(jsonLd).replace(/<\//g, '<\\/');
  return `<script type="application/ld+json" id="static-jsonld">${json}</script>`;
}

function renderRoute(shell, route) {
  const fullTitle = pageTitle(route);
  const url = publicUrl(route.path);
  let html = shell;

  html = setTitle(html, fullTitle);
  html = setMeta(html, 'meta[name="description"]', 'name', 'description', route.description);
  html = setMeta(html, 'meta[name="robots"]', 'name', 'robots', 'index,follow,max-image-preview:large');
  html = setLink(html, 'canonical', { href: url });
  html = setLink(html, 'llms', { href: '/llms.txt' });

  html = setMeta(html, 'meta[property="og:title"]', 'property', 'og:title', fullTitle);
  html = setMeta(html, 'meta[property="og:description"]', 'property', 'og:description', route.description);
  html = setMeta(html, 'meta[property="og:url"]', 'property', 'og:url', url);
  html = setMeta(html, 'meta[property="og:type"]', 'property', 'og:type', 'website');
  html = setMeta(html, 'meta[property="og:site_name"]', 'property', 'og:site_name', SITE_NAME);
  html = setMeta(html, 'meta[property="og:image"]', 'property', 'og:image', `${SITE_URL}/og-card.png`);

  html = setMeta(html, 'meta[name="twitter:card"]', 'name', 'twitter:card', 'summary_large_image');
  html = setMeta(html, 'meta[name="twitter:title"]', 'name', 'twitter:title', fullTitle);
  html = setMeta(html, 'meta[name="twitter:description"]', 'name', 'twitter:description', route.description);
  html = setMeta(html, 'meta[name="twitter:image"]', 'name', 'twitter:image', `${SITE_URL}/og-card.png`);
  html = setMeta(html, 'meta[name="twitter:url"]', 'name', 'twitter:url', url);

  html = html.replace(/\s*<script type="application\/ld\+json" id="static-jsonld">.*?<\/script>/s, '');
  html = html.replace('</head>', `    ${jsonLdScript(route.jsonLd)}\n  </head>`);

  const crawlerContent = `
    <main class="seo-crawler-content" aria-label="${escapeHtml(route.title)}">
      ${route.body.trim()}
      <p><a href="/llms">LLM product brief</a> | <a href="/llms.txt">Plain-text LLM brief</a> | <a href="/sitemap.xml">Sitemap</a></p>
    </main>
  `;
  html = html.replace('<div id="root"></div>', `<div id="root">${crawlerContent}</div>`);
  return html;
}

function routeOutputPath(route) {
  if (route.path === '/') return join(dist, 'index.html');
  return join(dist, route.path.replace(/^\//, ''), 'index.html');
}

function sitemapXml() {
  const routeUrls = routes.map((route) => ({
    loc: publicUrl(route.path),
    lastmod: UPDATED,
    changefreq: route.changefreq,
    priority: route.priority,
  }));
  routeUrls.push({
    loc: `${SITE_URL}/llms.txt`,
    lastmod: UPDATED,
    changefreq: 'monthly',
    priority: '0.6',
  });

  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${routeUrls.map((url) => `  <url>
    <loc>${url.loc}</loc>
    <lastmod>${url.lastmod}</lastmod>
    <changefreq>${url.changefreq}</changefreq>
    <priority>${url.priority}</priority>
  </url>`).join('\n')}
</urlset>
`;
}

const shell = await readFile(shellPath, 'utf8');

for (const route of routes) {
  const out = routeOutputPath(route);
  await mkdir(dirname(out), { recursive: true });
  await writeFile(out, renderRoute(shell, route), 'utf8');
}

await writeFile(join(dist, 'sitemap.xml'), sitemapXml(), 'utf8');

console.log(`SEO prerendered ${routes.length} route HTML files and sitemap.xml`);
