# Security Policy

## Supported Versions

Only the latest release receives security fixes.

## Reporting a Vulnerability

Please **do not** open a public issue for security problems.

Report privately using GitHub's **"Report a vulnerability"** button under the
repository's **Security** tab (Private vulnerability reporting).

Please include:
- A description of the issue and its impact
- Steps to reproduce
- Your Windows version and app version

You can expect an initial response within a reasonable time. Fixes will be
released as a new version, and reporters will be credited if they wish.

## Update Mechanism

The app checks for updates in the background and downloads them from a
GitHub-hosted Velopack update feed. This is separate from this repository's
Releases page, which only contains the installer.

Velopack verifies the checksums listed in the feed before installing a
package. Updates are currently **not code-signed**, so Windows SmartScreen
may warn about the installer. To verify an installer manually, compare its
SHA256 hash with the one listed in the release notes (newer releases).
