# Security Policy

## Reporting a vulnerability

**Do not open a public GitHub issue for a security vulnerability.**

### In a VA deployment

If you found the issue on a system operated by the Department of Veterans Affairs (any `*.va.gov` host running
this software), report it through the **VA Vulnerability Disclosure Program**, which is the authorized route for
security researchers and VA staff alike:

- Policy and submission form: <https://www.va.gov/vulnerability-disclosure-policy/>
- VA employees and contractors also notify their **ISSO** and the VA Cybersecurity Operations Center (CSOC)
  through the standard incident-reporting channel for the system's SSP.

The VDP defines what testing is authorized on VA systems. Testing against a VA deployment outside that policy is
not authorized by this project.

### In the source code

If the issue is in this repository rather than a specific deployment (a bug in the API, the admin SPA, the public
site, the migrations or the CI workflows), use **GitHub private vulnerability reporting** on this repository
(*Security → Report a vulnerability*). The report goes only to the maintainers. Please include:

- the component and commit or release affected;
- steps to reproduce, or a proof-of-concept request/response;
- the impact as you understand it (which control in `docs/SECURITY_CONTROLS.md` it bypasses, if you can tell).

You will get an acknowledgement within **3 business days** and a status update at least every **14 days** until the
issue is closed. Fixes are released as a normal pull request into `main` with a `security` label and a
`GHSA` advisory once a VA deployment has had the chance to patch; credit is given in the advisory unless you ask
otherwise.

## Scope

In scope: everything in this repository — `src/api`, `src/admin`, `src/public`, `migrations`, `infra`,
`.github/workflows` — and the default configuration described in `docs/DEPLOYMENT.md`.

Out of scope: vulnerabilities in the hosting stack (Windows Server, IIS, SQL Server, Active Directory / AD FS)
unless this project's documented configuration causes them; vulnerabilities in third-party dependencies that are
already reported upstream (please still tell us so we can bump the version); denial-of-service findings that
require more than the documented rate limits allow; findings that need `Auth:Mode=DevBypass`, which only runs in
the Development environment.

## Supported versions

Only `main` is supported. Every merge to `main` runs the full test suite, CodeQL, dependency-advisory and
secret scans and produces a CycloneDX SBOM (`.github/workflows/ci.yml`, `security.yml`); a VA deployment is
expected to track `main` through its own change-management process (`docs/DEPLOYMENT.md` § Blue-green update).

## What the project does about security

- Threat model and control implementation: [`docs/SECURITY_CONTROLS.md`](docs/SECURITY_CONTROLS.md)
  (NIST SP 800-53 mapping with the file and test behind each control).
- Hardening and operations: [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) (IIS STIG checklist, TLS, TDE, secrets,
  key rotation, retention).
- Logging and audit: [`docs/LOGGING.md`](docs/LOGGING.md), `docs/DATABASE_LAYER.md` § 4.9.
- Runtime settings that affect security (`auth.*`, `security.*`, `api.rateLimits.*`, `webhooks.*`,
  `media.*`): [`docs/SETTINGS.md`](docs/SETTINGS.md).
- Known gaps are listed in `docs/SECURITY_CONTROLS.md` § 8 and tracked under epic #152.
