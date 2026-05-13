# Contributors

Toast Notification was built in 2020 — full-stack, by two men working before
any of this AI noise was real. ASP.NET Core for the wire. A Windows agent that
spoke the OS-level notification API directly. PostgreSQL because the bills were
real and SQL Server licensing was theft. SignalR because every endpoint needed
a live channel and a poll loop was a confession of failure.

It served MSPs during the COVID-19 work-from-home explosion when help desks
were drowning. 986,000 messages delivered across 17 production tenants. The
problem it solved — fleet-wide OS-level notifications without dragging Teams or
Slack into the room — was real. The execution was solid. Then Teams and Slack
filled most of that gap, and the project went quiet.

The architecture didn't rot. The product didn't rot. The problem still exists
for the shops where OS-level fleet notification still matters — MSPs managing
endpoints in environments without chat apps, school districts that can't roll
out Slack, law firms that ban Teams on workstations. So the project came back.

In 2026 it came back with a new operating model. An AI team, running on the
[DocPro](https://docpro.cloud) platform, refining and extending what those two
men built. Carl Jeeter does the architecture review and arbitrates. Anthony
Catawampus implements and deploys. Diana Reyes owns anything visual. Abish
Lamman runs Code Sweep on every commit. They are personas, but they are not
pretend — they make real calls, they push back when something is wrong, they
catch the things a tired human at 11 PM would miss, and their memory persists
across sessions. They are not the product. They are how the product is
maintained.

This page is the credit roll. None of it is fake. None of it is hype.

---

## People

- **Keith Lucier** — Founder, original full-stack builder, product direction,
  code review, deployment. The man whose name is on the cert.
  [@keithrlucier](https://github.com/keithrlucier)

- **Original co-builder** — Toast Notification was built by two men in 2020.
  The second is uncredited here at his request. The work is his too.

---

## Platform

- **DocPro.Cloud** — Multi-agent development platform. Provides the team
  session formats (dev meeting, milestone, troubleshooting, build mode),
  persona memory persistence, and the Code Sweep methodology that gates every
  commit before it ships. team@docpro.cloud · https://docpro.cloud

- **Claude (Anthropic)** — Foundation model behind every team persona.
  Opus 4.6, Opus 4.7, and Sonnet 4.6 depending on the work. The team runs on
  it; it does not run the team. noreply@anthropic.com

---

## Attribution

Every commit authored through DocPro carries a
`Co-Authored-By: DocPro.Cloud <team@docpro.cloud>` trailer, applied
automatically by `.githooks/prepare-commit-msg`. Enable per-clone with:

```
git config core.hooksPath .githooks
```

Commits also carry the model-specific Claude trailer for whichever Anthropic
model authored the work.

---

## How to contribute

This is a passion project. Pull requests welcome. Issues get triaged when they
get triaged. If you ship something useful, send it — see
[README.md](README.md) for the project's posture.
