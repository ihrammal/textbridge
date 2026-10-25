# TextBridge 1.1.1

This maintenance update improves Custom setup, language menus, validation feedback and update handling.

- Custom setup offers Arabic, English and French recognition models separately, with folder and shortcut choices. Express installs the recommended complete configuration.
- Upgrading from 1.1.0 preserves the existing language models. Later updates preserve Custom model selections.
- The document-language menu includes all seven supported combinations and identifies the current selection.
- Missing or incomplete models produce guidance before recognition. Diagnostics checks the selected profile.
- Settings validation messages remain available inside the settings dialog.
- Updates preserve per-user or machine-wide installation scope. Startup feed checks have a bounded timeout; downloaded installers must pass SHA-256 verification.
- The bilingual About panel includes selectable author and contact information.

Existing installations with startup update checks enabled can discover this release through the same GitHub update feed. Users who disabled startup checks can use Help > Check for Updates. Updating requires consent; machine-wide installation may request Windows administrator permission.

Windows 64-bit. OCR remains local. Application and installer are unsigned. Review recognized text, especially names and numbers. Automated verification cannot guarantee perfect OCR or compatibility with every screen-reader configuration.

Choose TextBridge-Setup-1.1.1.exe for installation. The portable ZIP must be extracted with its entire folder. ClearRead-Setup-1.1.1.exe is an identical compatibility asset for older update clients. Third-party dependencies and their licenses are unchanged; their notices are included. Previously published dependency evidence and corresponding source packages remain available in the earlier releases.
