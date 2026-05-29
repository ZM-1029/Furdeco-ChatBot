# Software Development Contract

**Project:** Furdeco ChatBot – "Ask Frankie by Furdeco"
**Date:** 21 May 2026
**Last Updated:** 21 May 2026
**Document Reference:** FDC-2026-001

---

## 1. Parties

| | |
|---|---|
| **Client** | Furdeco |
| **Contact** | info@furdeco.co.uk |
| **Phone** | +44 (0) 121 285 5255 |
| **Website** | https://furdeco.gsit.co.uk |
| **Developer** | \[Developer / Agency Name\] |
| **Developer Contact** | \[Email\] |

---

## 2. Project Overview

The Developer agrees to design, develop, and deliver a web-based customer service chatbot application branded as **"Ask Frankie by Furdeco"**. The system allows Furdeco's customers to track furniture deliveries, confirm or decline delivery, add delivery instructions, and authenticate via One-Time Password (OTP).

The application is built as an embeddable web component that can be integrated into existing Furdeco customer-facing websites via a lightweight JavaScript embed script.

In addition, the system includes a **Daily Activity Reporting module** that automatically captures all API interactions, generates a colour-coded multi-sheet Excel workbook, and emails it to the business owner every day at a configured UK time.

---

## 3. Scope of Work

### 3.1 Core Chatbot Features

| Feature | Description |
|---|---|
| Order Tracking | Search orders by carrier reference or order number; verify identity by postcode |
| Delivery Confirmation | Customers can confirm or decline an upcoming delivery |
| Delivery Instructions | Customers can add special instructions (e.g. gate codes, safe places) |
| Order Notes | Customers can add notes to their orders |
| OTP Authentication | Two-factor identity verification via SMS or Email before sensitive operations |
| Embed Script | Lightweight JavaScript snippet (`embed.js`) for third-party site integration |

### 3.2 Daily Reporting Module (Added 21 May 2026)

| Feature | Description |
|---|---|
| API Request Logging | Every `/api/chat/*` call is captured (endpoint, reference, postcode, type, response time, status) in a daily JSONL file under `ReportLogs/` |
| Excel Report Generation | 4-sheet colour-coded Excel workbook generated daily — Dashboard, Detail Log, Endpoint Breakdown, Hourly Breakdown |
| Automated Email Delivery | Report emailed to `atripathi@zouma.ai` every day at **07:00 UK time** via existing Zoho Mail SMTP |
| Historical Log Parser | `/api/report/from-log?path=` endpoint parses existing Serilog log files and generates retrospective reports on demand |
| UK Timezone | All timestamps, hourly charts, and report headers use UK time (GMT/BST); timezone-safe on both Windows and Linux |
| Report Preview | `/api/report/preview` endpoint returns a sample report with dummy data for design review |

#### Excel Workbook Structure

| Sheet | Contents |
|---|---|
| **Dashboard** | KPI grid (total calls, track requests, data found/not found, OTP sessions, confirms, declines, user input, peak hour, avg/min/max response time, OTP success rate) + hourly activity table + endpoint summary |
| **DD-Mon (Detail Log)** | Every API call with date, time (UK), endpoint, reference/order no., postcode, type, response time (ms), status — colour coded green = found, salmon = error/not found |
| **Endpoint Breakdown** | Per-endpoint totals, success/fail counts, avg/min/max response time, success rate % |
| **Hourly Breakdown** | 24-row table (one per UK hour) with per-endpoint counts and daily totals footer |

### 3.3 Technical Deliverables

- ASP.NET Core 6.0 MVC web application
- RESTful API controllers (order tracking, confirmation, decline, instructions, notes)
- OTP service with 5-minute expiry and in-memory storage
- SMS integration (VoodooSMS API)
- Email integration (Zoho Mail SMTP) with branded HTML templates
- Structured logging via Serilog (daily rolling log files)
- Embeddable chatbot UI (Razor view, vanilla JavaScript, responsive CSS)
- **API request logging middleware** (`ApiLoggingMiddleware`) — captures all chat API calls non-intrusively
- **Daily report hosted service** (`DailyReportHostedService`) — background scheduler using ASP.NET Core `BackgroundService`
- **Excel report generator** (`ExcelReportService`) — ClosedXML-based, 4-sheet colour-coded workbook
- **Serilog log parser** (`LogFileParserService`) — parses existing Serilog files for retrospective reporting
- Deployment configuration for IIS / IIS Express / Azure App Service
- Source code hosted on GitHub repository

### 3.4 Integrations

| Service | Purpose |
|---|---|
| GSIT API (`furdeco.gsit.co.uk`) | Order data, tracking, status updates |
| VoodooSMS | OTP and notification SMS delivery |
| Zoho Mail (SMTP) | OTP, notification email delivery, and daily report distribution |
| ClosedXML (NuGet) | Excel workbook generation |

### 3.5 Out of Scope

- Mobile native applications (iOS / Android)
- Backend order management system (provided by GSIT)
- Hosting infrastructure provisioning (Client's responsibility)
- Ongoing content or order data management
- Business intelligence dashboards or database-backed analytics

---

## 4. Technology Stack

| Layer | Technology |
|---|---|
| Backend Framework | ASP.NET Core 6.0 |
| Architecture | MVC (Model-View-Controller) + BackgroundService |
| Frontend | HTML5, CSS3, Vanilla JavaScript |
| Fonts | Google Fonts (Syne, DM Sans) |
| Logging | Serilog (structured, daily rolling files) |
| Report Generation | ClosedXML 0.104 |
| SMS Provider | VoodooSMS |
| Email Provider | Zoho Mail (SMTP) |
| Scheduler | ASP.NET Core `BackgroundService` (no external scheduler required) |
| Version Control | Git / GitHub |
| Target Runtime | .NET 6.0 |
| Cloud Platform | Microsoft Azure App Service (Windows or Linux) |

---

## 5. Brand Guidelines

The Developer shall adhere to the following Furdeco brand specifications:

| Element | Value |
|---|---|
| Primary Green | `#3AB54A` |
| Dark Green | `#2a8f38` |
| Light Green | `#4fd45f` |
| Chatbot Name | Ask Frankie by Furdeco |
| Support Email | info@furdeco.co.uk |
| Support Phone | +44 (0) 121 285 5255 |
| Support Hours | Monday – Saturday, 8am – 8pm |

---

## 6. Deliverables & Milestones

| Milestone | Deliverable |
|---|---|
| M1 – Project Setup | Repository, project scaffolding, CI/CD configuration |
| M2 – Core API | Order tracking, confirmation, decline, and notes endpoints |
| M3 – OTP & Auth | SMS and email OTP service, identity verification flow |
| M4 – Chatbot UI | Embedded chat interface, responsive design, brand styling |
| M5 – Integrations | GSIT API, VoodooSMS, and Zoho Mail fully integrated |
| M6 – Daily Reporting | API logging middleware, Excel report generator, scheduled email delivery |
| M7 – Historical Reporting | Serilog log file parser, on-demand retrospective report endpoint |
| M8 – Azure Readiness | Timezone cross-platform fix (Windows/Linux), Always On configuration guidance, persistent log directory |
| M9 – Testing & QA | Unit testing, integration testing, end-to-end QA |
| M10 – Deployment | Azure App Service deployment, production configuration, handover |

---

## 7. Configuration Reference

### 7.1 Daily Report Settings (`appsettings.json`)

```json
"DailyReport": {
  "RecipientEmail": "atripathi@zouma.ai",
  "SendHourUK": 7,
  "SendMinuteUK": 0,
  "LogDirectory": "D:\\home\\LogFiles\\ReportLogs"
}
```

### 7.2 Azure App Service Requirements

| Requirement | Setting |
|---|---|
| Always On | **Enabled** — required to keep the background scheduler running |
| OS | Windows or Linux — both supported (timezone handled automatically) |
| Log Directory | Set `DailyReport:LogDirectory` to `D:\home\LogFiles\ReportLogs` for persistent storage |
| Minimum Tier | Basic B1 or above (Always On not available on Free/Shared) |

---

## 8. Client Responsibilities

- Provide valid API credentials for GSIT, VoodooSMS, and Zoho Mail
- Provide hosting environment (Azure App Service, Basic tier minimum, with Always On enabled)
- Configure `DailyReport:LogDirectory` to a persistent path on the hosting environment
- Review and approve deliverables at each milestone
- Designate a point of contact for timely feedback and decisions
- Supply brand assets (logos, additional design guidelines) if required

---

## 9. Developer Responsibilities

- Deliver all features defined in Section 3
- Follow Furdeco brand guidelines (Section 5)
- Maintain clean, documented, version-controlled source code
- Secure sensitive credentials before production deployment (environment variables / secrets vault)
- Ensure the daily report scheduler operates correctly in the UK timezone across all deployment environments
- Provide reasonable bug fixes for defects identified within the warranty period

---

## 10. Security Obligations

The Developer shall:

- Store API keys and credentials as environment variables or a secrets manager in production (not in source-controlled `appsettings.json`)
- Implement OTP expiry (5-minute TTL) to limit authentication window
- Apply appropriate CORS and frame-ancestor policies
- Not expose customer PII in logs or error messages
- Restrict the `/api/report/from-log` and `/api/report/preview` endpoints or remove them before public production deployment

---

## 11. Intellectual Property

Upon full payment, all custom source code, assets, and documentation produced under this contract are assigned to the Client. Third-party libraries and services remain governed by their respective licences.

---

## 12. Confidentiality

Both parties agree to keep confidential all proprietary information exchanged during this engagement, including but not limited to API credentials, customer data, and business processes. This obligation survives termination of the contract.

---

## 13. Warranty

The Developer warrants the delivered software against defects for **30 days** following final delivery. Defects reported within this period will be remediated at no additional charge. This warranty does not cover changes to third-party APIs or services outside the Developer's control.

---

## 14. Limitation of Liability

The Developer's total liability under this contract shall not exceed the total fees paid. Neither party shall be liable for indirect, consequential, or special damages.

---

## 15. Termination

Either party may terminate this contract with **14 days' written notice**. Upon termination, the Client shall pay for all work completed to date, and the Developer shall deliver all completed deliverables and source code.

---

## 16. Governing Law

This contract is governed by the laws of **England and Wales**. Any disputes shall be resolved in the courts of England and Wales.

---

## 17. Change Log

| Version | Date | Change |
|---|---|---|
| 1.0 | 21 May 2026 | Initial contract — core chatbot features |
| 1.1 | 21 May 2026 | Added Daily Reporting Module (Section 3.2), Azure deployment requirements (Section 7.2), updated milestones and technology stack |

---

## 18. Signatures

By signing below, both parties agree to the terms of this contract.

| | Client | Developer |
|---|---|---|
| **Name** | | |
| **Title** | | |
| **Signature** | | |
| **Date** | | |

---

*This document was last updated on 21 May 2026.*
