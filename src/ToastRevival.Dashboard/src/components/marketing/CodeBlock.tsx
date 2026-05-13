import { useEffect, useRef, useState } from 'react';

type CodeBlockProps = {
  code: string;
  language?: string;
  label?: string;
};

export function CodeBlock({ code, language, label }: CodeBlockProps) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (timer.current !== null) {
        window.clearTimeout(timer.current);
      }
    };
  }, []);

  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      if (timer.current !== null) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setCopied(false), 1800);
    } catch {
      setCopied(false);
    }
  };

  const headingLabel = label ?? language;

  return (
    <div className="m-docs-code">
      <div className="m-docs-code-head">
        <span className="m-docs-code-label">{headingLabel ?? 'code'}</span>
        <button
          type="button"
          className="m-docs-code-copy"
          onClick={onCopy}
          aria-label={copied ? 'Copied' : 'Copy code to clipboard'}
        >
          {copied ? (
            <>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M20 6 L 9 17 L 4 12" />
              </svg>
              Copied
            </>
          ) : (
            <>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <rect x="9" y="9" width="11" height="11" rx="2" />
                <path d="M5 15 L 5 5 C 5 3.9 5.9 3 7 3 L 15 3" />
              </svg>
              Copy
            </>
          )}
        </button>
      </div>
      <pre>
        <code>{code}</code>
      </pre>
    </div>
  );
}

type CalloutProps = {
  kind?: 'note' | 'warning';
  title?: string;
  children: React.ReactNode;
};

export function Callout({ kind = 'note', title, children }: CalloutProps) {
  return (
    <div className={`m-docs-callout m-docs-callout--${kind}`}>
      {title && <p className="m-docs-callout-title">{title}</p>}
      <div className="m-docs-callout-body">{children}</div>
    </div>
  );
}
