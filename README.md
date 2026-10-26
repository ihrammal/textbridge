# TextBridge 1.1.0 — installation and accessibility fixes

TextBridge is a Windows application by Iman Rammal that turns Arabic, English and French images and scanned PDFs into editable text.

Version 1.1.0 replaces Tesseract with a choice of PaddleOCR PP-OCRv5 or Windows OCR. Both recognize text locally. Paddle includes a private interpreter and language models; Windows OCR uses installed Windows language packs. Searchable PDF export works with either engine.

**[Download TextBridge 1.1.0 for Windows (64-bit)](https://github.com/ihrammal/textbridge/releases/download/v1.1.0/TextBridge-Setup-1.1.0.exe)**

[Release page, portable build, source and checksums](https://github.com/ihrammal/textbridge/releases/tag/v1.1.0). The installer is unsigned. The package includes the offline OCR runtime and language models.

The initial 24 generated samples matched exactly with Paddle. A separate 10-sample printed Arabic trial measured 0.20% character errors for Paddle versus 0.31% for Tesseract. These are small test sets, not a general accuracy guarantee. [Read the verification scope and limitations](docs/OCR-UPGRADE-1.1.0.md).

Handwriting, historical calligraphy, unusual fonts, tables and complex columns can require correction. Review names and numbers. Arabic review items may cover an entire line because reliable individual word positions are not always available.

Free for noncommercial use. Commercial use requires prior written permission from iman.rammal@gmail.com. See LICENSE. Third-party components retain their own licenses. Earlier MIT-licensed copies keep that license.

![TextBridge showing Arabic, English and French demonstration text](marketing/1.1.0/textbridge-app-en.png)

## Installation choices

- **Express:** select Install to accept the license and install PaddleOCR with the recommended desktop and Start menu shortcuts.
- **Custom:** choose PaddleOCR or Windows OCR, select Arabic, English and/or French models, review the license, choose the install folder, Start menu group and shortcuts, then install. Starting the app after setup is optional.

The download contains Paddle's files for offline installation. Choosing Windows OCR skips those files and saves installed disk space; it does not reduce the download size. Add missing Windows OCR language packs through Windows Settings. The app reports missing packs instead of silently changing engines.

Settings > App settings > OCR lets you choose the engine. Paddle must be installed to select it; rerun setup to add it later. Windows OCR does not provide confidence scores or Paddle's page-orientation classifier.

## Interface and menus

Choose Settings > App settings > General > Interface language, then Save.
Arabic switches the menus, settings, review and report labels to right-to-left.
English is also available. Document language is independent of interface language.
The welcome/message language can be selected separately under Accessibility.

File contains open, save and export. Edit contains editing and table conversion.
Recognize contains extraction and optional online text improvement. Review contains
words to check. Settings is the only location for accessibility preferences.
“Text to check” contains words or Arabic lines the recognition engine is less certain about; other errors can still exist. Confidence values are line estimates. Review names and numbers.

## Saving organized text

File > Save saves editable UTF-8 text. File > Export offers Word, HTML, Markdown
and searchable PDF. Settings > App settings > OCR includes an option to keep
line breaks and spacing. Word retains detected headings and paragraphs; HTML
preserves whitespace. Searchable PDF retains original page appearance. Complex
columns and tables can still require manual review. The spacing option is off
by default because extra spaces can reduce reading usability in Arabic.

## Automatic updates

Help > Check for Updates checks the configured HTTPS feed, downloads a newer
stable installer, verifies its SHA-256 checksum and starts installation. Unsaved
text must be saved or explicitly discarded first. Windows may ask for administrator
permission. Settings > App settings > Updates controls checks at startup.
These requests never include document contents and are independent of the option
to prevent document text from being sent to online correction providers.

The update source is the public ihrammal/textbridge GitHub releases feed. Only
newer stable releases with an installer and SHA-256 digest are offered.
See docs/UPDATE-HOSTING.md for the format and remaining installation validation.

## Local build and checks

Use scripts/build-windows.ps1 to build the self-contained Windows application in
publish/TextBridge.exe, and scripts/build-installer.ps1 for the installer. Keep the
entire publish folder together. scripts/test-windows.ps1 runs the functional suite,
including actual local OCR, exports, interface switching and simulated update downloads. Build prerequisites and pinned Paddle runtime setup are documented in docs/BUILD-WINDOWS.md.
Historical publication and QA notes under docs describe earlier versions, not the
status of this local revision.

TextBridge was previously named Clear Read. Existing settings and protected API keys
are migrated automatically. The GitHub repository address remains unchanged.

## Author

Iman Rammal

- Braille Training Specialist
- NVDA Certified Expert
- Microsoft Word with NVDA Certified Expert
- JAWS Certified
- Assistive technology trainer
- Translator / interpreter

Email: iman.rammal@gmail.com

- YouTube: https://www.youtube.com/@ihrammal
- Telegram: https://t.me/ihrammal
- Facebook: https://www.facebook.com/ihrammal
- X: https://x.com/ihrammal
- TikTok: https://tiktok.com/ihrammal
