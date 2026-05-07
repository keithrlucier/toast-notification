# ToastRevival — Design Specification
**Author**: Diana Reyes
**Status**: Initial spec — will expand before M4 implementation

---

## Design System Foundation

One design system. Website and dashboard. No exceptions.

### Typography
- **Primary**: Inter — UI text, headings, body copy. Variable font, all weights.
- **Mono**: JetBrains Mono — code snippets, API keys, device IDs, technical content.
- **Scale**: 12 / 14 / 16 / 20 / 24 / 32 / 40 / 48px. Body is 14px. Nothing smaller than 12px. Ever.
- **Line height**: 1.5 for body, 1.2 for headings. No exceptions.
- **Weight**: 400 (body), 500 (labels/emphasis), 600 (subheadings), 700 (headings).

### Spacing
- **Base grid**: 8px.
- **Values**: 4 (micro-adjustments only), 8, 16, 24, 32, 48, 64, 96.
- **Component padding**: 16px minimum internal padding on all interactive elements.
- **Section spacing**: 48px between major sections, 24px between related groups.

### Color System
```
Foundation:
  --bg-primary:       #0F1117    (app background, dark)
  --bg-secondary:     #1A1D27    (card/panel background)
  --bg-tertiary:      #242836    (elevated surfaces, hover states)
  --bg-surface:       #FFFFFF    (light mode content areas)

  --text-primary:     #F0F0F5    (high contrast, dark bg)
  --text-secondary:   #B0B0C4    (muted text, dark bg)
  --text-dim:         #7A7A92    (disabled, hints, dark bg)
  --text-dark:        #1A1D27    (text on light backgrounds)

Brand:
  --accent-primary:   #00C9A7    (teal — inherited from original brand, modernized)
  --accent-hover:     #00E5BF    (teal lighter)
  --accent-pressed:   #00A88C    (teal darker)

Status:
  --status-success:   #4ADE80
  --status-warning:   #FBBF24
  --status-error:     #F87171
  --status-info:      #60A5FA

Notification Preview:
  --preview-bg:       #202020    (Windows 11 dark theme desktop color)
  --preview-card:     #2D2D2D    (Windows 11 notification card bg)
  --preview-text:     #FFFFFF    (Windows notification text)
  --preview-subtext:  #C0C0C0    (Windows notification secondary text)
```

### Elevation / Shadows
- **Level 0**: Flat (background surfaces)
- **Level 1**: `0 1px 3px rgba(0,0,0,0.3)` (cards, dropdowns)
- **Level 2**: `0 4px 16px rgba(0,0,0,0.4)` (modals, popovers)
- **Level 3**: `0 8px 32px rgba(0,0,0,0.5)` (notification preview panel)

### Border Radius
- **Small**: 4px (buttons, inputs, badges)
- **Medium**: 8px (cards, panels)
- **Large**: 12px (modals, notification preview)
- **Never**: Full rounded (pill shapes). This isn't a toy.

---

## Notification Template Gallery

Six curated templates. Each has locked visual hierarchy. The MSP fills in content — they do NOT design.

### 1. Announcement
- **Use case**: Company news, policy updates, general information
- **Tone**: Neutral, informative
- **Layout**: Title + 2-line body + optional hero image + optional single action button
- **Default audio**: ms-winsoundevent:Notification.Default

### 2. Alert
- **Use case**: Security warnings, system issues, urgent IT notices
- **Tone**: Urgent, attention-grabbing
- **Layout**: Title (bold) + 2-line body + optional hero image + 2 action buttons (Acknowledge / Dismiss)
- **Default audio**: ms-winsoundevent:Notification.Looping.Alarm
- **Scenario**: urgent (breaks through Do Not Disturb on supported builds)

### 3. Action Required
- **Use case**: Password resets, software approvals, compliance tasks
- **Tone**: Direct, clear CTA
- **Layout**: Title + 1-line body + 2 action buttons (primary action + Remind Later)
- **Default audio**: ms-winsoundevent:Notification.Reminder

### 4. Reminder
- **Use case**: Meetings, deadlines, maintenance windows
- **Tone**: Helpful, non-intrusive
- **Layout**: Title + 2-line body (includes date/time) + optional action button
- **Scenario**: reminder
- **Default audio**: ms-winsoundevent:Notification.Reminder

### 5. Celebration
- **Use case**: Birthdays, milestones, team wins, welcome messages
- **Tone**: Warm, positive
- **Layout**: Title + 1-line body + hero image (branded celebration graphic)
- **Default audio**: ms-winsoundevent:Notification.Default

### 6. Maintenance
- **Use case**: Scheduled downtime, update windows, system reboots
- **Tone**: Matter-of-fact, includes timeframe
- **Layout**: Title + 2-line body (what, when, impact) + 2 action buttons (Details / Acknowledge)
- **Default audio**: ms-winsoundevent:Notification.Default

---

## Live Preview Panel

The notification composer includes a preview panel on the right side that renders a pixel-accurate representation of the Windows toast notification.

### Requirements
- Dark background (#202020) simulating Windows 11 desktop
- Notification card matches Windows 11 notification styling (#2D2D2D, rounded corners, Segoe UI font in preview)
- **Preview uses Segoe UI** (Windows system font) — NOT Inter. The preview must match what Windows renders.
- Updates in real-time as user types
- Shows character count warnings when approaching truncation limits
- Shows hero image at correct 364x180 ratio
- Shows app logo at correct 48x48
- Shows action buttons in Windows style
- Responsive — collapses below the composer on narrow viewports

---

## Admin Dashboard Layout

### Navigation
- Left sidebar, collapsible to icon-only on narrow viewports
- Sections: Dashboard, Notifications (compose/history), Devices, Templates, Assets, Settings, Audit Log
- Tenant name + logo at top of sidebar
- User avatar + role badge at bottom

### Dashboard (Home)
- Key metrics: Active devices, notifications sent (7d/30d), delivery rate, interaction rate
- Recent notifications with status indicators
- Device health summary (connected/disconnected/stale)
- Quick-send button (opens composer with most-used template)

### Notification Composer (Full Page)
- Left: Template selector + content fields + target picker + schedule options
- Right: Live preview panel (fixed position, scrolls with content)
- Bottom: Send button with broadcast confirmation dialog when targeting > 100 devices

---

## Marketing Site (toastnotification.com)

### Structure
1. **Hero**: Product screenshot (actual dashboard), headline, single CTA
2. **Problem/Solution**: MSPs need to communicate with endpoints. Scripts are fragile. RMM notifications are ugly. This is neither.
3. **Features**: 3-4 key features with screenshots
4. **How It Works**: 3-step visual (Deploy agent → Design notification → Send)
5. **Pricing**: Transparent tiers with device counts
6. **CTA**: Get started free (10 devices)
7. **Footer**: Links, legal, social

### Rules
- No stock photos. No AI-generated hero art. Product screenshots only.
- No gradient blobs. No floating geometric shapes. We're selling to IT professionals, not wellness apps.
- No animations that don't serve a purpose. The notification preview can animate (toast sliding in). The hero can have a subtle parallax. Everything else stays still.
- Dark theme to match the product aesthetic.
- Mobile-responsive. MSP decision-makers browse on their phones at lunch.

---

## Things I Will Not Compromise On

1. The live preview must match Windows rendering. If it doesn't match, we don't ship.
2. Templates are curated, not customizable beyond content. No freeform canvas. No font picker. No color wheel for notification text.
3. The 8px grid applies to everything. If a developer eyeballs spacing, I will know.
4. Character count validation is not optional. If Windows truncates text, our composer prevents it.
5. The notification preview panel uses Segoe UI. The rest of the app uses Inter. These are different contexts and I will fight anyone who tries to unify them.
