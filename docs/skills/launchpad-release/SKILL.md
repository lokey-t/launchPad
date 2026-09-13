---
name: launchpad-release
description: Prepare and publish LaunchPad Windows releases to GitHub and Gitee, including bilingual documentation, portable ZIPs, single EXE setup and incremental assets. Use for this repository's version bumps and authorized release publishing.
---

# LaunchPad release

Work from the repository root containing LaunchPad.csproj. Remotes are normally origin (https://github.com/lokey-t/launchPad.git) and gitee (https://gitee.com/lokey-t/launchPad.git); verify them. main is the release branch. website/ is an ignored independent repository with no shared publishing remote; do not add or deploy it as part of a desktop release.

## Prepare

- Check status, changes since the last release, remote main and tags. Preserve user changes. Do not force-push or overwrite published tags/assets. If a remote advanced, fetch and reconcile before continuing.
- Use numeric project versions (e.g. 0.97), normalized four-part AssemblyVersion/FileVersion (0.97.0.0), and existing tag format v0.97. The updater does not accept v.0.97.
- Update LaunchPad.csproj and Installer/LaunchPad.Setup.csproj, README.md, README.en.md and bilingual RELEASE_NOTES.md. Describe actual behavior and supported packages, not unreleased intentions.
- Run the isolated BackupTests, SortingTests, UpdateTests and SetupTests. SetupTests accept an app publish directory; build them with PayloadDirectory and -t:Rebuild for --ui or --payload. Never test by installing over the user's active copy.
- Build all assets with `./tools/Build-Release.ps1 -Version VERSION -Dotnet PATH`. Requires Windows, .NET 8 SDK, PowerShell with Get-FileHash; no Python packages. Output must be fresh. If an old build exists, inspect and move that exact output aside within bin before rebuilding; never recursively delete computed paths.
- The script generates full portable ZIP, framework-dependent ZIP, offline setup EXE, both incremental manifests, deduplicated lp-SHA.gz assets and checksums under bin/releases/vVERSION. Keep ZIP layouts as complete multi-file publishes. Do not single-file-publish the app used for incremental manifests; only the setup bootstrap is single-file.
- Check EXE/DLL and manifest versions, ZIP contents, all local checksums and that all manifest references exist. Runtime and deployment flavor must match. Read docs/INSTALLER.md and docs/UPDATES.md when changing either protocol.

## Commit and publish

Authorization to prepare does not itself authorize pushing or publishing. If the user already explicitly requested committing and releasing to these hosts, that authorization is sufficient; do not ask again.

1. Review staged files and secrets; keep build output, credentials and temporary probes out of Git. Generate a descriptive commit subject/body from final changes, written to a file and passed to git commit -F.
2. Commit the intended source/doc changes, create a lightweight vVERSION tag on that commit, and push main plus that tag to both remotes without force. Verify both remote hashes. Stop if the same tag exists on a different commit.
3. Use tools/Publish-Release.py (standard-library Python) with host github or gitee. Credentials come from existing Git Credential Manager or GITHUB_TOKEN/GITEE_TOKEN in memory. Never print credential-fill output, store tokens, embed them in URLs, or include them in commits.
4. Run `python tools/Publish-Release.py HOST prepare --version VERSION --commit FULL_SHA --execute`. GitHub starts as a draft; Gitee as a prerelease so the updater ignores an incomplete release.
5. Run `python tools/Publish-Release.py HOST upload --version VERSION --assets bin/releases/vVERSION --execute`. It checks local checksums, resumes matching existing assets, refuses replacements and verifies remote bytes/digests. Repeat only after inspecting a concrete transient failure; on persistent platform limits or permissions, stop the affected operation and report the limitation without falsely claiming completion.
6. Once both uploads are complete, run `python tools/Publish-Release.py HOST publish --version VERSION --assets bin/releases/vVERSION --execute` for each host. This verifies every asset before marking stable. Use verify mode for read-only rechecks.
7. Confirm public release state, tag commit, three end-user packages, both manifests and all referenced assets. Check application update discovery against the public release. Report commit and both release links, and any actual limitations.

Observed platform constraint: Gitee rejected the 129 MB v0.97 installer with HTTP 400 and a 100 MB attachment limit. When this occurs, keep the single EXE intact on GitHub and disclose a direct GitHub download link in both README languages and the release notes. Use --external-installer on Gitee upload/verify/publish; the tool verifies that exact installer's GitHub digest and requires GitHub to be stable before publishing Gitee. ZIPs and incremental assets remain mirrored. Do not silently omit the installer or split it into multiple user downloads. GitHub drafts may require discovery through the release list rather than the tag endpoint.

Gitee may return 403 for all downloads after bulk verification, including previously readable checksum files. Stop repeated bulk retries. If every expected attachment is already uploaded, local hashes and GitHub digests are verified, and Gitee names/sizes match, verify/publish supports --metadata-only to finish without repeated downloads. Explicitly report that this checks Gitee attachment completeness, not every remote file hash. Do not use it to bypass a known checksum mismatch or missing attachment. The updater can fall back to GitHub when Gitee downloads fail.

GitHub official REST release assets: https://docs.github.com/en/rest/releases/assets
Gitee upload API: POST /api/v5/repos/{owner}/{repo}/releases/{release_id}/attach_files, multipart field file; download uses the corresponding attachment ID route. Check current host limits rather than assuming them.

## Skill distribution

The repository copy lives at docs/skills/launchpad-release. Copy the complete folder into the current agent's skill directory (normally ~/.codex/skills) when installing it for discovery; do not hard-code the original developer's username or workspace. Keep both copies in sync when updating this workflow. Validate with skill-creator's quick_validate.py when available.
