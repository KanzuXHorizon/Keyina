# Security policy

## Supported versions

Keyina is currently in active pre-release development. Security fixes are applied to the latest commit on `main`. No stable binary release is supported yet.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability.

Use GitHub's private vulnerability reporting for this repository:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Include the affected commit or version, impact, reproduction steps, and any proposed mitigation.

Please remove passwords, API keys, private documents, and unrelated personal data from reports. A minimal proof of concept is preferred over real user data.

If private vulnerability reporting is temporarily unavailable, contact the repository owner through their GitHub profile and request a private reporting channel. Do not publish exploit details before a coordinated fix is available.

## Security boundaries

Keyina is designed around these boundaries:

- The native typing hot path is offline.
- Password and secure input contexts should pass physical input through unchanged.
- Injected keyboard events carry a private marker and must not be reprocessed.
- Speech is opt-in and separate from ordinary typing.
- Speech credentials belong in Windows Credential Manager, never repository or configuration files.
- Unknown hook, native-engine, IPC, or compatibility failures should fall back to literal input rather than blocking the keyboard.

Reports that show a boundary can be bypassed are especially important.

## Response process

Maintainers will aim to:

- Confirm receipt through the private report.
- Reproduce and assess severity.
- Prepare a focused fix and regression test.
- Coordinate disclosure and credit with the reporter.
- Publish remediation details after affected users can update.

Response times are best-effort while the project is maintained by a small team.

## Out of scope

The following are normally not security vulnerabilities by themselves:

- Missing code signing in development builds.
- Expected antivirus warnings for unsigned local builds.
- Speechmatics service availability or account policy.
- Bugs that require an already compromised administrator account.
- Compatibility failures that safely pass literal input through without exposing data or escalating privilege.
