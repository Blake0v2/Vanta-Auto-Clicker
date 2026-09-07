# Free Microsoft Store distribution

Vanta can be submitted as an MSIX desktop app. Microsoft signs MSIX packages after Store certification; no paid signing certificate is needed for that distribution route. A locally built unsigned MSIX is **not** a public download that fixes Smart App Control. Existing GitHub EXE downloads remain unsigned.

## Current status

The build script creates an x64 package for Windows 10 version 2004 or newer and Windows 11. It bundles the existing WPF app, original logo, font/license, and Store user guide. The Store executable opens Microsoft Store for updates and excludes the GitHub download/install service at compile time. The direct EXE build keeps its existing updater.

Account identity, packaged installation testing, and Microsoft certification are still required. No Store listing has been created or submitted by these scripts. Free distribution does not require changing Vanta's source-code license.

## Account setup (owner)

1. Go to [Microsoft Store developer registration](https://storedeveloper.microsoft.com/) and register an Individual account with your Microsoft account using the free onboarding flow. Complete Microsoft's identity verification yourself; do not send passwords or identity documents to this project/chat.
2. Create a new app and reserve **Vanta Auto Clicker**, if available. Choose the packaged **MSIX** route, rather than submitting a URL to the existing Setup EXE.
3. Open the app's **Product identity** page. Copy `Package/Identity/Name`, `Package/Identity/Publisher`, and `PublisherDisplayName` exactly.
4. Copy `identity.example.json` to `identity.local.json` and replace its placeholders with those values. Set `DisplayName` to the exact reserved name. These are product identifiers, not signing keys.

## Build

Run from the project root in Windows PowerShell, with the Windows SDK packaging tools installed:

```powershell
# Compile, run Store unit tests, and validate a placeholder package locally:
.\scripts\Build-Store.ps1 -Preview

# After receiving the actual Partner Center identity:
.\scripts\Build-Store.ps1 -IdentityPath .\store\identity.local.json

# Build and exercise the Store UI, including bounded clicks in its test pad:
.\build.ps1 -Store -UiTest
```

Review the scripts before running them. If restored/downloaded scripts are blocked by PowerShell execution policy, use a process-scoped development invocation, such as `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Store.ps1 -Preview`. This is only for building reviewed source locally; it does not sign the output or change Smart App Control on anyone's PC.

Preview output goes to `artifacts/store-preview` and is explicitly named **PREVIEW-NOT-FOR-DISTRIBUTION**. Actual-identity submission output goes to `dist/store`. `last-build.json` records the staged manifest and package path. Staging is a fresh directory under `build/store` on every build. The script runs MakePri and MakeAppx with manifest validation enabled and never signs, installs, uploads, or publishes anything.

Use the first three components of the app version for release numbering and keep the fourth component zero for Store submission. This initial package uses the existing 1.0.6.0 app version. Increase the version before subsequent Store submissions.

## Finish and publish

1. Test the package under its actual identity on a development PC or VM. Verify Start launch, shortcuts, hold-mode release, mouse buttons, limits, sequence capture, profile import/export, update navigation, DPI scaling, and uninstall. The ordinary executable/UI tests do not substitute for this packaged-context test. Use Microsoft's supported development deployment or Partner Center package flight; do not ask customers to install a test certificate or disable protection.
2. Upload the **unsigned MSIX** from `dist/store` to the app's Packages section. Microsoft handles production signing after certification.
3. Complete the listing, age ratings, availability, free pricing, support details, privacy-policy URL, screenshots, and the `runFullTrust` justification. Draft listing and reviewer text are in `SUBMISSION.md`. Confirm the listing answers yourself before submission.
4. Submit for review. Approval is Microsoft's decision; a successful local package build is not certification.
5. Once approved, test a Store installation on a clean Windows PC with Smart App Control enabled. Replace the website's primary download link with the **actual Store product URL**. Do not link it to the unsigned preview MSIX. Store-managed updates apply to Store installations; users of the earlier direct build install the Store edition separately and can export/import settings.

## References

- [Free registration and MSIX signing FAQ](https://learn.microsoft.com/en-us/windows/apps/publish/faq/get-started-with-the-microsoft-store)
- [Manual MSIX packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- [Desktop packaging compatibility](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)
- [Store signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
