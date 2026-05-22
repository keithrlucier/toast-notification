import { Link } from 'react-router-dom';
import { useSeo, breadcrumbLd } from '../../../lib/seo';

export default function Terms() {
  useSeo({
    title: 'Terms of Service',
    description:
      'Toast2IT, LLC terms of service — the agreement governing use of the Toast Notification service.',
    path: '/legal/terms',
    jsonLd: breadcrumbLd([
      { name: 'Home', path: '/' },
      { name: 'Terms of Service', path: '/legal/terms' },
    ]),
  });

  return (
    <div className="m-security-page">
      <div className="m-security-inner">

        <header className="m-security-header">
          <p className="m-eyebrow">Legal</p>
          <h1>Terms of Service</h1>
          <p className="m-security-lede">
            These Terms of Service (&ldquo;Terms&rdquo;) govern your access to and use of the
            Toast Notification service operated by Toast2IT, LLC (&ldquo;Company,&rdquo;
            &ldquo;we,&rdquo; &ldquo;us,&rdquo; or &ldquo;our&rdquo;). By registering for or
            using the Service, you agree to these Terms. If you do not agree, do not use the
            Service.
          </p>
          <div className="m-security-meta">
            <span>Effective date: May 16, 2026</span>
            <span>Company: Toast2IT, LLC</span>
            <span>Contact: <a href="mailto:legal@toastnotification.com">legal@toastnotification.com</a></span>
          </div>
        </header>

        <section className="m-security-section" aria-labelledby="acceptance-heading">
          <h2 id="acceptance-heading">1. Acceptance of Terms</h2>
          <p>
            By creating an account, accessing, or using the Service, you represent that (a) you
            are at least 18 years old; (b) you have the authority to bind yourself or the
            organization on whose behalf you are acting; and (c) your use of the Service will
            comply with these Terms and all applicable laws and regulations.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="service-heading">
          <h2 id="service-heading">2. Description of Service</h2>
          <p>
            Toast Notification is a managed Windows toast notification platform designed for
            managed service providers (MSPs) and IT administrators. The Service allows
            authorized administrators to send branded Windows toast notifications to enrolled
            endpoint devices through a multi-tenant web dashboard and API.
          </p>
          <p>
            We reserve the right to modify, suspend, or discontinue any aspect of the Service
            at any time with reasonable notice where practicable. We will not be liable to you
            or any third party for any modification, suspension, or discontinuation of the
            Service.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="accounts-heading">
          <h2 id="accounts-heading">3. Accounts and Registration</h2>
          <ul>
            <li>
              You must provide accurate and complete information during registration and keep
              your account information current.
            </li>
            <li>
              You are responsible for maintaining the confidentiality of your account
              credentials and for all activity that occurs under your account.
            </li>
            <li>
              You must notify us immediately at{' '}
              <a href="mailto:legal@toastnotification.com">legal@toastnotification.com</a> if
              you suspect unauthorized access to your account.
            </li>
            <li>
              One account may be used to manage one tenant organization. Sub-accounts (users)
              within a tenant are permitted subject to your subscription tier.
            </li>
            <li>
              Account access may be subject to an approval process. We reserve the right to
              decline or revoke access at our discretion.
            </li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="use-heading">
          <h2 id="use-heading">4. Acceptable Use</h2>
          <p>You agree not to use the Service to:</p>
          <ul>
            <li>Send notifications that are deceptive, fraudulent, harassing, defamatory, or otherwise unlawful</li>
            <li>Transmit malware, spam, or unsolicited commercial communications</li>
            <li>Circumvent authentication, access controls, or tenant isolation mechanisms</li>
            <li>Interfere with or disrupt the integrity or performance of the Service or its infrastructure</li>
            <li>Reverse engineer, decompile, or attempt to derive source code from any part of the Service</li>
            <li>Use the Service to infringe any third-party intellectual property rights</li>
            <li>Resell or sublicense access to the Service without our prior written consent</li>
            <li>Use automated tools (bots, scrapers) against Service endpoints beyond what the documented API permits</li>
          </ul>
          <p>
            We may suspend or terminate accounts that violate these restrictions without prior
            notice.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="billing-heading">
          <h2 id="billing-heading">5. Subscription and Billing</h2>
          <ul>
            <li>
              <strong>Free Trial</strong> &mdash; Trial accounts are subject to device and
              duration limits and require approval. Trial access does not constitute a
              commitment to continued service.
            </li>
            <li>
              <strong>Managed SaaS</strong> &mdash; Paid subscriptions are billed in advance
              on a monthly basis. Fees are non-refundable except as required by applicable law
              or as expressly stated in these Terms.
            </li>
            <li>
              <strong>Roll Your Own</strong> &mdash; Self-hosted deployments using the
              provided Docker distribution are subject to these Terms but are not billed by us.
            </li>
            <li>
              We may change pricing with at least 30 days&rsquo; notice. Continued use of the
              Service after the effective date of a price change constitutes acceptance of the
              new pricing.
            </li>
            <li>
              You are responsible for all applicable taxes. We will add taxes where required
              by law.
            </li>
            <li>
              Failure to pay may result in suspension or termination of your account.
            </li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="data-heading">
          <h2 id="data-heading">6. Data and Privacy</h2>
          <p>
            Our collection and use of personal information is governed by our{' '}
            <Link to="/legal/privacy">Privacy Policy</Link>, which is incorporated into these
            Terms by reference.
          </p>
          <p>
            You retain ownership of all content and data you submit to the Service
            (&ldquo;Customer Data&rdquo;). You grant Toast2IT, LLC a limited license to store,
            process, and transmit Customer Data solely as necessary to provide the Service.
          </p>
          <p>
            You are solely responsible for the content of notifications you send through the
            Service and for ensuring that your use of Customer Data complies with applicable
            privacy laws, including any obligations to your end users regarding data
            collection and processing.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="ip-heading">
          <h2 id="ip-heading">7. Intellectual Property</h2>
          <p>
            Toast2IT, LLC retains all right, title, and interest in and to the Service,
            including all associated software, documentation, and intellectual property. These
            Terms do not grant you any ownership rights; you receive only a limited,
            non-exclusive, non-transferable license to use the Service as described herein.
          </p>
          <p>
            Feedback, suggestions, or feature requests you submit may be used by us without
            restriction or compensation to you.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="disclaimers-heading">
          <h2 id="disclaimers-heading">8. Disclaimers</h2>
          <p>
            THE SERVICE IS PROVIDED &ldquo;AS IS&rdquo; AND &ldquo;AS AVAILABLE&rdquo; WITHOUT
            WARRANTIES OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
            WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, OR
            NON-INFRINGEMENT. WE DO NOT WARRANT THAT THE SERVICE WILL BE UNINTERRUPTED,
            ERROR-FREE, OR FREE OF HARMFUL COMPONENTS.
          </p>
          <p>
            Windows notification delivery depends on Microsoft Windows operating system
            behaviors, endpoint configuration, and network conditions that are outside our
            control. We do not guarantee delivery of any specific notification.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="liability-heading">
          <h2 id="liability-heading">9. Limitation of Liability</h2>
          <p>
            TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, TOAST2IT, LLC AND ITS OFFICERS,
            DIRECTORS, EMPLOYEES, AND AGENTS WILL NOT BE LIABLE FOR ANY INDIRECT, INCIDENTAL,
            SPECIAL, CONSEQUENTIAL, OR PUNITIVE DAMAGES, OR ANY LOSS OF PROFITS, REVENUE,
            DATA, BUSINESS, OR GOODWILL ARISING OUT OF OR RELATED TO THESE TERMS OR YOUR USE
            OF THE SERVICE, EVEN IF WE HAVE BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.
          </p>
          <p>
            OUR TOTAL AGGREGATE LIABILITY TO YOU FOR ANY CLAIM ARISING OUT OF OR RELATED TO
            THESE TERMS OR THE SERVICE WILL NOT EXCEED THE GREATER OF (A) THE AMOUNTS YOU
            PAID TO US IN THE TWELVE (12) MONTHS PRECEDING THE CLAIM OR (B) ONE HUNDRED
            DOLLARS (USD $100).
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="indemnification-heading">
          <h2 id="indemnification-heading">10. Indemnification</h2>
          <p>
            You agree to indemnify, defend, and hold harmless Toast2IT, LLC and its officers,
            directors, employees, and agents from and against any claims, liabilities, damages,
            judgments, awards, losses, costs, expenses, or fees (including reasonable attorneys&rsquo;
            fees) arising out of or relating to your violation of these Terms or your use of
            the Service, including any Customer Data you submit or notifications you send.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="termination-heading">
          <h2 id="termination-heading">11. Termination</h2>
          <p>
            Either party may terminate these Terms at any time. You may terminate by
            discontinuing use of the Service and closing your account. We may terminate or
            suspend your access at any time for violation of these Terms, non-payment, or any
            other reason at our discretion, with or without notice.
          </p>
          <p>
            Upon termination, your right to access the Service immediately ceases. Provisions
            that by their nature should survive termination (including intellectual property,
            disclaimers, limitation of liability, and governing law) will survive.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="law-heading">
          <h2 id="law-heading">12. Governing Law and Disputes</h2>
          <p>
            These Terms are governed by the laws of the United States and the State of
            Delaware, without regard to conflict of law principles. Any dispute arising
            out of or relating to these Terms or the Service will be resolved exclusively
            in the state or federal courts located in Delaware, and you consent to personal
            jurisdiction in those courts.
          </p>
          <p>
            Before initiating any formal proceeding, the parties agree to attempt to resolve
            any dispute in good faith through direct negotiation for a period of 30 days
            after written notice of the dispute.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="changes-heading">
          <h2 id="changes-heading">13. Changes to These Terms</h2>
          <p>
            We may update these Terms from time to time. We will notify you of material
            changes by email and by updating the effective date above. Continued use of the
            Service after the effective date constitutes acceptance of the revised Terms. If
            you do not agree to the revised Terms, you must stop using the Service.
          </p>
        </section>

        <section className="m-security-section" aria-labelledby="general-heading">
          <h2 id="general-heading">14. General</h2>
          <ul>
            <li>
              <strong>Entire agreement</strong> &mdash; These Terms and the Privacy Policy
              constitute the entire agreement between you and Toast2IT, LLC regarding the
              Service and supersede all prior agreements.
            </li>
            <li>
              <strong>Severability</strong> &mdash; If any provision is found unenforceable,
              it will be modified to the minimum extent necessary, and the remaining
              provisions will continue in full force.
            </li>
            <li>
              <strong>Waiver</strong> &mdash; Our failure to enforce any provision does not
              constitute a waiver of our right to enforce it in the future.
            </li>
            <li>
              <strong>Assignment</strong> &mdash; You may not assign these Terms without our
              prior written consent. We may assign these Terms without restriction.
            </li>
          </ul>
        </section>

        <section className="m-security-section" aria-labelledby="contact-heading">
          <h2 id="contact-heading">15. Contact</h2>
          <p>
            For questions about these Terms, please contact:
          </p>
          <p>
            Toast2IT, LLC<br />
            <a href="mailto:legal@toastnotification.com">legal@toastnotification.com</a>
          </p>
        </section>

        <div className="m-security-footer-nav">
          <Link to="/" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            &larr; Back to home
          </Link>
          <Link to="/legal/privacy" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            Privacy Policy
          </Link>
          <a href="mailto:legal@toastnotification.com" className="m-btn m-btn-ghost" style={{ fontSize: 14 }}>
            legal@toastnotification.com
          </a>
        </div>

      </div>
    </div>
  );
}
