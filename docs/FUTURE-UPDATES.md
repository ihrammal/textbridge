# Files retained for future TextBridge updates

Keep src, tests, scripts, help, resources, installer, docs, .github, LICENSE,
README.md, global.json and Git configuration files. The Git checkout and history
are in .tools/textbridge-github.

Keep runtime for the pinned offline OCR models, private Python, PDF/image tools
and licenses. Keep .tools/dotnet, .tools/inno, .tools/7zip and review-python for
rebuilding and verification. Keep publish together as the current runnable app.

artifacts retains the corrected installers, source package, release manifests,
verification logs, dependency evidence and publication receipts. The original
published 1.1.0 installer is preserved under previous-release-1.1.0 for recovery
and upgrade verification. User PDFs are preserved in the project root.

The displayed maintenance version remains 1.1.0. The corrected internal build
1.1.0.1 and matching release tag allow old update clients to discover it. Keep
the four-part TextBridge installer filename and legacy ClearRead compatibility
alias. See CORRECTED-BUILD-1.1.0.md. Future build identifiers must increase so
installed clients can detect corrections, even when the displayed version stays
the same. Do not silently overwrite a stable update tag with different bytes.

Before publishing, validate source, run functional tests, verify help, build and
test installation/reinstallation/uninstallation, and require successful Windows
CI on the exact source commit. Record SHA-256 values and verify public downloads.
Keep marketing captions/images and their generator for future announcements.

Old QA app copies, obsolete installers and generated bin outputs can be removed
after validation. They can be rebuilt from the retained source and runtime.
Do not delete source, models, build tools, user documents or release provenance.
