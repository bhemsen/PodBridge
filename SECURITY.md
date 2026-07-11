# Security Policy

PodBridge is a local-only, driver-free-by-default tray app: it does not run
with admin rights in its default tier, does not phone home, and only reads
local Bluetooth Low Energy advertisements and local audio policy. This policy
describes what's currently supported and how to report a vulnerability.

## Supported Versions

PodBridge has not yet cut a numbered release; only the latest release (once
published) is supported with security fixes. Older releases do not receive
patches — please always upgrade to the latest release before reporting an
issue against an older build.

| Version         | Supported          |
| --------------- | ------------------- |
| Latest release  | :white_check_mark: |
| Older releases  | :x:                 |

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities privately via **GitHub Private Vulnerability Reporting**:
go to the repository's **Security** tab and select **Report a vulnerability**
(<https://github.com/bhemsen/PodBridge/security/advisories/new>). This is the
**sole** channel for reporting a security issue in this project.

What happens next:

- The maintainer triages reports privately.
- If confirmed, a fix is prepared and disclosed via a **GitHub Security
  Advisory** once available, on the maintainer's own timeline — there is no
  fixed SLA, as this is a single-maintainer open-source project.
- A **CVE is requested only if the reporter asks for one**; it is not
  requested by default.

Thank you for helping keep PodBridge and its users safe.
