# Changelog

## 1.0.1 - Unreleased

### Production hardening and .NET 8+ support

- clarified the package support contract as a `net8.0` library for **.NET 8+** consuming applications
- introduced secure defaults for browser startup, keeping risky behavior behind explicit opt-ins only
- hardened BrowserPool provisioning, page ownership tracking, and cleanup so disposal stays within library-owned resources

### Migration notes

- callers relying on permissive browser flags must now opt in explicitly with `AllowInsecureCertificates` and/or `DisableSandbox`
- deployments that disable automatic browser downloads should configure `ExecutablePath` or a managed browser cache strategy before enabling `SkipBrowserDownload`
- invalid template, HTML, URL, file, and PDF option inputs now fail earlier in the request path with clearer validation messages
