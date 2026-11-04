# Releasing

Releases are published to nuget.org by `.github/workflows/release.yml` using
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub issues a signed OIDC token
to the publishing job, nuget.org checks it against a trusted publishing policy and returns an API key that is valid for
one hour. No long-lived NuGet API key is created or stored.

## One-time setup

1. **nuget.org:** sign in with the account that owns `Nast.Html2Pdf`, open your user menu → **Trusted Publishing** and
   add a policy:

   | Field | Value |
   |---|---|
   | Repository Owner | `NastMz` |
   | Repository | `Html2Pdf` |
   | Workflow File | `release.yml` (file name only) |
   | Environment | `nuget` |

   A new policy can start as *temporarily active* for 7 days; the first successful publish from the workflow makes it
   permanently active. If it lapses before then, restart the 7-day window from the same page.

2. **GitHub:** in **Settings → Environments → `nuget`**, add the secret **`NUGET_USER`** containing the nuget.org
   **profile name** of the policy owner (not the e-mail address). Optionally add required reviewers so every publish
   needs an approval. A `NUGET_API_KEY` secret is no longer used and can be deleted.

## Publishing a version

1. Make sure `<Version>` in `src/Nast.Html2Pdf/Nast.Html2Pdf.csproj` on `main` is the version to release and that
   `CHANGELOG.md` describes it.
2. Create and **publish** a GitHub release whose tag is `v` + that version (for example `v2.0.0`) targeting `main`:

   ```bash
   gh release create v2.0.0 --repo NastMz/Html2Pdf --target main --title "v2.0.0" --notes "See CHANGELOG.md"
   ```

3. The workflow then:
   - runs the complete CI pipeline (`verify`) on the tagged revision;
   - checks that the tag, the project version and the source revision recorded in the package all match, and verifies
     the package checksums;
   - obtains a short-lived key through Trusted Publishing and pushes the verified `.nupkg` (and its `.snupkg`);
   - attaches the package, symbols, `SHA256SUMS` and `SOURCE_REVISION` to the GitHub release.

If `verify` fails for an infrastructure reason (for example a macOS job that never received a runner), use
**Re-run failed jobs** on the run; publishing waits for verification to succeed.

Versions pushed to nuget.org cannot be deleted, only unlisted. For a trial run, release a pre-release version such as
`2.0.1-rc.1` (set it in the project file and tag `v2.0.1-rc.1`).

## Troubleshooting

| Symptom in the `publish` job | Cause |
|---|---|
| `Set the NUGET_USER secret …` | The secret is missing from the `nuget` environment |
| NuGet login fails with a policy or token error | The policy fields do not match (owner, repository, workflow file `release.yml`, environment `nuget`), the policy is inactive, or `NUGET_USER` is not the policy owner's profile name |
| `403` on push | The policy's scopes or package pattern do not allow pushing `Nast.Html2Pdf` |
| `tag … does not match project version` | The tag and `<Version>` disagree; fix one of them and create a new release |
