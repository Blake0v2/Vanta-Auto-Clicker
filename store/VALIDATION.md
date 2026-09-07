# Local validation — 2026-09-06

Status: local Store packaging preparation completed; public distribution is not fixed until an actual-identity package is tested, certified, and available through Microsoft Store.

Completed on the restored Windows development PC:

- Store build: 33 unit tests passed, including verification that the GitHub updater class is absent from the compiled assembly.
- Direct build: 35 unit tests passed, including existing GitHub version/checksum validation.
- Store executable UI suite: 19 tests passed in the interactive desktop, including global hook registration, distribution-specific update text, embedded fonts, all views, real left/middle/right clicks in the owned test pad, a limited double-click sequence, and own-window protection. The sandbox initially denied cursor access; the same bounded suite passed outside it.
- MakePri generated a single resource index containing all included DPI variants. MakeAppx packed the x64 preview with manifest validation enabled.
- Inspected the preview archive: placeholder identity, only the intended app executable, one resource index, no Setup EXE or production signature. SHA-256 matched the recorded build hash.
- Submission build rejects absent identity, unchanged example placeholders, and conflicting preview/identity arguments.
- Native screenshot inspection passed after making its backdrop non-activating. Direct-build website previews were refreshed and the source-freshness check passed.
- Git whitespace validation passed.

The Store executable was exercised as a desktop process. Installation/activation under MSIX package identity, Windows App Certification Kit validation, Store updates/uninstall, and a clean-PC Smart App Control installation test are **not yet completed**. These remain necessary before declaring public distribution ready.

The preview package lives in `artifacts/store-preview` and must not be sent to users as an installer. Existing `dist` EXEs were not replaced by the Store build, and no release or Store listing was published.
