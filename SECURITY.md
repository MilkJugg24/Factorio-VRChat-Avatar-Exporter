# Security Policy

## Supported Versions

Security issues are reviewed and addressed for all released versions of the Factorio VRChat Avatar Exporter.

| Version | Supported |
|---------|-----------|
| All released versions | ✅ |

Because this project is maintained by a single developer, security fixes may be released in the latest version only. Users are encouraged to update to the most recent release when a security update becomes available.

## Reporting a Vulnerability

If you discover a security vulnerability, please do **not** post its details in a public issue.

Instead, report it privately: on this repository's **Security** tab, choose **Report a vulnerability**
(GitHub's private vulnerability reporting). Only the maintainer sees the report.

If you can't use that, [open an issue](../../issues/new) that says only that you have a security report
to make, with no details of the problem, and the maintainer will arrange a private way to send it.

Please include:

- A description of the issue
- Steps to reproduce the problem
- Potential impact
- Suggested mitigations (if known)

## Response Expectations

The maintainer will make a reasonable effort to:

1. Acknowledge receipt of a report.
2. Investigate the reported issue.
3. Determine whether the issue is a legitimate security concern.
4. Develop and release a fix when appropriate.
5. Credit the reporter, if desired.

Response times may vary due to the project being maintained by a single developer.

## Scope

This security policy applies to:

- The Factorio VRChat Avatar Exporter source code
- Official releases published through this repository
- GitHub Actions workflows contained in this repository

This security policy does **not** apply to:

- Third-party software
- User-created avatar packs
- VRChat avatars, models, textures, or other user-supplied content
- External services and dependencies outside the maintainer's control

## Security Updates

When a security issue is confirmed, fixes will be published through the normal project release process. Users should monitor the repository's releases page and update to the latest release when security-related updates are announced.

## Checking a Download

Every release lists the SHA-256 checksum of its two files in its notes. To make sure a file you were
sent is the one published here, compare its checksum with the one in the notes (Windows PowerShell:
`Get-FileHash <file>`; macOS: `shasum -a 256 <file>`; Linux: `sha256sum <file>`).

Files published after this section was added also carry a signed build provenance attestation: proof
that the file was built by this repository's release workflow. With the
[GitHub CLI](https://cli.github.com):

```sh
gh attestation verify <file> --repo MilkJugg24/Factorio-VRChat-Avatar-Exporter
```

Avatar packs are made by their authors, not published here, and have no checksum or attestation. A
pack is a Factorio mod: install packs only from people you trust, and see "Validation" in
`docs/vrc-avatar-format.md` for the check that refuses a pack carrying scripts.

## Disclosure Policy

Please allow a reasonable amount of time for investigation and remediation before publicly disclosing a vulnerability. Coordinated disclosure helps protect users while fixes are being developed and tested.

The maintainer reserves the right to determine severity, remediation approach, and disclosure timelines based on the nature of the reported issue.
