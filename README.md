# VA CMS — USWDS-Compliant Content Management System

A modern, self-hosted content management system built on the **U.S. Web Design System (USWDS)** for VA and federal agency web properties. Built with **React** (frontend) and **Microsoft SQL Server** (backend), designed to replace SharePoint 2016 on-premises deployments.

## Why This Exists

SharePoint 2016 on-prem is aging out. Drupal 11 (the only cleanly TRM-authorized CMS) requires significant PHP/Linux expertise most VA dev teams don't carry. This project gives VA teams a purpose-built CMS that:

- Ships 100% USWDS-compliant UI out of the box
- Runs on the Windows/.NET/MSSQL stack most VA teams already own and operate
- Lets **content owners** publish and manage content without a developer
- Gives **dev teams** full extensibility to build custom content types, workflows, and embedded applications
- Meets VA TRM, Section 508, and FedRAMP requirements by design

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18 + TypeScript |
| Design System | USWDS 3.x (Web Components + CSS tokens) |
| API | ASP.NET Core 8 Web API |
| Database | Microsoft SQL Server 2019+ |
| Auth | Azure AD / Windows Auth (SAML/OIDC) |
| Search | SQL Full-Text Search (+ optional Elasticsearch) |
| File Storage | Network share / Azure Blob (configurable) |
| Hosting | IIS / Windows Server or containerized |

## Quick Links

- [Business Requirements Document](docs/BRD.md)
- [Claude Design Prompt](docs/CLAUDE_DESIGN_PROMPT.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Data Model](docs/DATA_MODEL.md)
- [API Reference](docs/API_REFERENCE.md)
- [Deployment Guide](docs/DEPLOYMENT.md)
- [Content Owner Guide](docs/CONTENT_OWNER_GUIDE.md)
- [Developer Guide](docs/DEVELOPER_GUIDE.md)

## Project Status

🟡 **Pre-development** — BRD and backlog complete. Ready for agent build.

## License

MIT
