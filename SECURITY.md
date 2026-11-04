# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 2.x | Yes: security fixes are released as patch versions |
| 1.x | No: its design cannot meet the security model of 2.x; upgrade (see `docs/migration-v2.md`) |

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's **Report a vulnerability** button
(Security → Advisories) on https://github.com/NastMz/Html2Pdf. Do not open public issues for undisclosed
vulnerabilities. You can expect an acknowledgement within five working days and a fix or mitigation plan for confirmed
high-severity issues within 30 days.

## Update expectations

- **Library and .NET dependencies.** Every CI run fails if the dependency graph (including transitive packages) has a
  known high or critical vulnerability, and a release cannot be published from a failing pipeline. Dependency updates
  that fix vulnerabilities are released as patch versions.
- **Chromium.** The browser is not bundled; operators install and update it. Track the stable channel and update at least
  monthly, and promptly when Chrome releases fix exploited vulnerabilities. New Chromium major versions are exercised by
  CI, which installs the current stable Chrome on every run.
- **.NET runtime.** Use a supported, patched .NET runtime. The package targets .NET 8 and is tested on .NET 8 and 10.
