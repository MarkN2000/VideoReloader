# Publishing

Add the markn2000 team's Thunderstore Service Account token as the repository secret `THUNDERSTORE_TOKEN` in GitHub Settings > Secrets and variables > Actions. Never commit the token.

## Release a new version

1. Update the version in `VideoReloader/VideoReloader.csproj` and the `ResonitePlugin` attribute in `VideoReloader/Plugin.cs`.
2. Run `dotnet run --project .\checks\VideoReloader.Checks.csproj -c Release`, then `.\Build-Package.ps1`.
3. Commit and push the changes. Create a draft GitHub Release tagged `vX.Y.Z` at that commit.
4. Attach `dist/VideoReloader-X.Y.Z.zip` before publishing the release.
5. Publish the release. The **Publish to Thunderstore** workflow validates and uploads the attached ZIP without rebuilding it.

The tag must match the manifest version. Drafts and prereleases are excluded. Existing Thunderstore versions are skipped, so use a new version for each package update. Categories are `Mods` and `AI Generated`.

## Validate or retry

In Actions > Publish to Thunderstore > Run workflow, enter an existing release tag. **Validate without publishing** is enabled by default and does not use the Thunderstore token. Validation checks the ZIP, version, Thunderstore version lookup, and the official CLI download checksum.

To retry an upload, run the workflow for that release with validation-only mode disabled. Unexpected Thunderstore API errors stop the workflow instead of treating the version as unpublished. If a workflow fix was needed, dispatch it from the updated `main` branch.

The GitHub token has read-only repository permissions. The Thunderstore token is passed only to the publishing step. The workflow downloads official tcli 0.2.4 and checks its pinned SHA-256 before execution.
