# Toast Notification - Platform v2

> Repo / project codename: **ToastRevival** (internal). Product / user-facing brand: **Toast Notification** (toastnotification.com).

## Overview

Full commercial revival of the Toast Notification platform — an enterprise push notification system for Managed Service Providers (MSPs). Enables MSPs to send branded, interactive Windows toast notifications to all managed endpoints from a centralized web dashboard.

## Current Status

**M0A complete (2026-05-07).** Signed MSI installs cleanly on Win11 lab machine, agent runs in user context, toast survives login and reboot. Up next: **M0 (Foundation & Deployment Validation)**, starting with M0 D2 (signed MSIX package for Store + Intune LOB).

See `STATUS.md` for the current project state.

## Origin

Originally built ~2020-2021 by Keith Lucier (toast2IT, LLC) with developer Anmol Rehan. Published to Microsoft Store (app ID: 9P5L0MRMFRRF). UWP client + ASP.NET Core backend + Azure AD B2C auth. Standalone product discontinued; Store listing still active.

## Revival Scope

Ground-up modernization using original source code as reference architecture:
- **Windows Agent**: WinUI 3 / .NET 8, MSIX-packaged, COM activator for full toast features
- **Backend API**: ASP.NET Core 8, SignalR for real-time push, EF Core + PostgreSQL
- **Admin Dashboard**: React web application with real-time notification designer
- **Marketing Site**: toastnotification.com redesign (Build Mode project)
- **Deployment**: Microsoft Store, MSIX sideload (Intune), MSI (RMM tools)

## Key Differentiators

- Only polished commercial product in the MSP notification space
- Web dashboard with live notification preview (WYSIWYG)
- Curated template gallery — impossible to make ugly
- Content moderation pipeline (Azure Content Safety)
- Multi-tenant with RBAC and broadcast controls
- Three deployment channels: Store, Intune, RMM

## Repository

GitHub: https://github.com/keithrlucier/toast
PAT stored in: `docs/ToastRevival/.env` (gitignored — never commit credentials)

## Reference Material

- Original source: `docs/toast/ToastNotification Source/ToastNotification.zip`
- Original DB backup: `docs/toast/ToastNotification Source/Notification.bak`
- Original install scripts: `docs/toast/ToastNotification Source/Beta Installation Files/`
- Microsoft Store: https://apps.microsoft.com/detail/9P5L0MRMFRRF
- Website: www.toastnotification.com
