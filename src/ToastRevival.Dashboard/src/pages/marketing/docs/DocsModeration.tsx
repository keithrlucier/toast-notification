import { Link } from 'react-router-dom';
import { CodeBlock } from '../../../components/marketing/CodeBlock';
import { useSeo, techArticleLd, breadcrumbLd } from '../../../lib/seo';

export default function DocsModeration() {
  useSeo({
    title: 'Content moderation',
    description:
      'How Toast Notification scans outgoing notifications, configures per-tenant moderation policy, and handles the admin approval queue.',
    path: '/docs/moderation',
    jsonLd: [
      techArticleLd({
        headline: 'Content moderation in Toast Notification',
        description:
          'Configure per-tenant scanning thresholds, custom blocklist terms, Azure Content Safety credentials, and the admin approval queue.',
        path: '/docs/moderation',
      }),
      breadcrumbLd([
        { name: 'Home', path: '/' },
        { name: 'Docs', path: '/docs' },
        { name: 'Content moderation', path: '/docs/moderation' },
      ]),
    ],
  });

  return (
    <article>
      <h1>Content moderation</h1>
      <p>
        Toast Notification runs every outgoing notification through a per-tenant moderation
        pipeline before it ships to endpoints. Administrators configure scanning policy,
        thresholds, and an admin approval queue from the dashboard. This page documents the
        pipeline, every configurable knob, and the operator surfaces that consume it.
      </p>

      <h2 id="pipeline">How the pipeline works</h2>
      <p>
        When the API accepts a <code>POST /api/notifications</code>, the request flows through
        three checks in order:
      </p>
      <ol>
        <li>
          <strong>Tenant blocklist.</strong> Title and body lines are matched, case-insensitively,
          against the tenant's banned-term list. A hit short-circuits to{' '}
          <strong>Block</strong>; no external scan is performed.
        </li>
        <li>
          <strong>Azure Content Safety text scan.</strong> The combined title + body is sent to
          Azure Content Safety, which returns severity scores (0–6) for hate, sexual content,
          violence, and self-harm.
        </li>
        <li>
          <strong>Azure Content Safety image scan.</strong> If the notification carries an
          ad-hoc <code>HeroImageUrl</code>, it is scanned the same way. Asset-library images
          approved at upload time are not re-scanned.
        </li>
      </ol>
      <p>
        The pipeline returns one of three decisions per notification:
      </p>
      <ul>
        <li>
          <strong>Pass</strong> — every category score is below the tenant's Review threshold.
          The notification queues for immediate delivery.
        </li>
        <li>
          <strong>Review</strong> — the worst category score is at or above the Review threshold
          but below the Block threshold. The notification is saved with status{' '}
          <code>PendingReview</code> and held until an administrator approves or rejects it from
          the <Link to="/moderation">Moderation queue</Link>. The API responds 202 Accepted; the
          notification does not enqueue.
        </li>
        <li>
          <strong>Block</strong> — the worst category score is at or above the Block threshold,
          or the blocklist matched. The API responds <code>422 Unprocessable Entity</code> with
          a body of <code>{'{ error: "content_blocked", message, scores }'}</code>. The
          notification is never persisted.
        </li>
      </ul>
      <p>
        Tenants that need human-in-the-loop on every send can enable{' '}
        <strong>Require admin approval for every notification</strong>, which routes every
        Pass-classified notification to the queue regardless of scan scores. Block-classified
        notifications continue to short-circuit; the override only escalates Pass to Review.
      </p>

      <h2 id="configure">Configure your tenant's moderation policy</h2>
      <p>
        Every setting on this page is configured from{' '}
        <Link to="/settings/tenant">Settings → Tenant</Link> → <strong>Content Moderation</strong>.
        Admin role (or higher) is required. Changes take effect on the next notification — there
        is no restart or deploy step.
      </p>

      <h3 id="scanning">Scanning toggles</h3>
      <dl className="m-docs-dl">
        <dt>Moderation enabled</dt>
        <dd>
          Master switch. When off, no Azure scan runs and the engine returns Pass for every
          notification. The blocklist still applies — disabling moderation does not disable
          your banned terms.
        </dd>

        <dt>Scan notification text</dt>
        <dd>
          Run the title and body through Azure Content Safety text moderation. Disabling this
          skips the text scan but leaves image scanning, blocklist, and the require-approval
          override in place.
        </dd>

        <dt>Scan hero images</dt>
        <dd>
          Run ad-hoc <code>HeroImageUrl</code> values through Azure Content Safety image
          moderation. Library assets approved on upload do not re-scan when used in
          notifications.
        </dd>

        <dt>Require admin approval for every notification</dt>
        <dd>
          Promote every Pass-classified notification to <strong>Review</strong>, routing it to
          the Moderation queue. Use this when policy or compliance requires a human signature
          on every outbound message.
        </dd>
      </dl>

      <h3 id="thresholds">Severity thresholds</h3>
      <p>
        Both thresholds use the Azure Content Safety severity scale: <strong>0–1</strong> is
        safe content, <strong>2–3</strong> is low-severity, <strong>4–5</strong> is
        medium-severity, and <strong>6</strong> is severe. The platform default is{' '}
        <strong>Review at ≥ 2</strong>, <strong>Block at ≥ 5</strong>, matching Microsoft's
        published guidance for general-purpose deployments.
      </p>
      <ul>
        <li>
          Lowering the Review threshold to <strong>1</strong> sends most borderline content to
          the queue. Use this for high-stakes tenants where false positives are cheaper than
          false negatives.
        </li>
        <li>
          Raising the Block threshold to <strong>6</strong> means only the most severe content
          is rejected outright, with everything between Review and Block going to the queue.
        </li>
        <li>
          The Block threshold must be greater than the Review threshold — otherwise
          Review-classified content would Block instead of queue. The API rejects PUTs that
          violate this invariant.
        </li>
      </ul>

      <h3 id="blocked-message">Custom blocked-content message</h3>
      <p>
        Sets the <code>message</code> field returned in the 422 body when the moderation engine
        blocks a notification. Senders see this in the dashboard's Compose error toast. Up to
        500 characters. Blocklist hits always surface the matched term — the custom message
        does not override that.
      </p>

      <h3 id="azure">Bring your own Azure Content Safety</h3>
      <p>
        Toast Notification ships with a platform-default Azure Content Safety resource that
        every tenant uses unless they configure their own. Bringing your own resource has two
        benefits:
      </p>
      <ul>
        <li>
          Scan calls are billed to your subscription, with cost line items visible in your
          Azure portal.
        </li>
        <li>
          Your tenant's content is processed in your subscription's region — useful for data
          residency or compliance frameworks that require tenant-isolated processing.
        </li>
      </ul>
      <p>
        Provision a Content Safety resource in Azure, then paste the endpoint and one of the
        two keys into the Settings card. The key is stored encrypted at rest and is never
        returned to the dashboard after first save — you will see a masked tail
        (<code>••••••••abcd</code>) on subsequent visits. Replace it by typing in a new key;
        leave the field blank to keep the existing key.
      </p>
      <p>
        To remove a custom key and revert to the platform default, clear the key field and
        save.
      </p>

      <h2 id="queue">The Moderation queue</h2>
      <p>
        Notifications with status <code>PendingReview</code> appear on the{' '}
        <Link to="/moderation">Moderation page</Link> alongside the tenant blocklist. The page
        is admin-only and lists:
      </p>
      <ul>
        <li>
          <strong>Pending review</strong> — notifications waiting for approval. Each row shows
          the sender, target type and device count, the moderation reason if any, and the
          original title and body lines. Approve sends the notification through the queue
          immediately; Reject sets the notification to <code>Failed</code> permanently.
        </li>
        <li>
          <strong>Blocklist</strong> — tenant-specific banned terms. Matches are case-
          insensitive substring matches against title and body. Up to 500 characters per term.
          Add and remove freely; changes take effect on the next notification.
        </li>
      </ul>

      <h2 id="api">API behavior</h2>
      <p>The send endpoint reports moderation outcomes on three response codes:</p>
      <CodeBlock
        language="http"
        code={`POST /api/notifications
202 Accepted
{
  "notificationId": "…",
  "status": "Queued"             // or "PendingReview" if held for admin approval
}

POST /api/notifications
422 Unprocessable Entity
{
  "error": "content_blocked",
  "message": "Content blocked by moderation policy.",
  "scores": { "Hate": 6, "Violence": 2, ... }   // null when blocked by blocklist
}`}
      />

      <p>
        The same <code>ModerationResultJson</code> field is persisted on every notification row
        (including Pass) so the moderation decision and per-category scores are queryable from
        the <Link to="/audit">Audit log</Link>.
      </p>

      <h2 id="reference">Quick reference</h2>
      <dl className="m-docs-dl">
        <dt>Where do I change the policy?</dt>
        <dd>
          <Link to="/settings/tenant">Settings → Tenant</Link>, the{' '}
          <strong>Content Moderation</strong> card. Admin role required.
        </dd>

        <dt>Where do I approve flagged notifications?</dt>
        <dd>
          <Link to="/moderation">Moderation</Link>, Pending Review tab. Admin role required.
        </dd>

        <dt>Where do I manage banned terms?</dt>
        <dd>
          <Link to="/moderation">Moderation</Link>, Blocklist tab. Admin role required.
        </dd>

        <dt>Can I disable moderation entirely?</dt>
        <dd>
          Yes — flip <em>Moderation enabled</em> off on the Settings card. The blocklist still
          applies and the require-approval override still works if separately enabled.
        </dd>

        <dt>Does the blocklist count against my Azure quota?</dt>
        <dd>
          No. Blocklist matching is in-process and runs before any Azure call. Blocklist hits
          never reach Azure.
        </dd>
      </dl>
    </article>
  );
}
