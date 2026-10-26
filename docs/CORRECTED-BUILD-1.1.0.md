# TextBridge 1.1.0 correction build 1

Displayed/product version: 1.1.0. Assembly/file/update version: 1.1.0.1.

The original installed 1.1.0 client compares release tag versions and requires an
exact installer asset name. Replacing the old 1.1.0 asset alone cannot notify it.
The stable update descriptor must therefore use tag v1.1.0.1 and asset
TextBridge-Setup-1.1.0.1.exe, with GitHub's SHA-256 digest. That installer must be
byte-identical to the corrected manual download TextBridge-Setup-1.1.0.exe.
Keep the existing v1.1.0 manual-download addresses available. The corrected client
checks all four version parts so it does not repeatedly offer the same build.

Before publishing either corrected download: finish automated checks, obtain the
owner's NVDA large-document progress/text-insertion confirmation, run CI on the
exact committed source, and test installer upgrade behaviour. Back up the old
release metadata/assets before replacing them. Verify both downloadable installer
names and hashes afterward. No corrected release has been published by this change.

Update checks are enabled by default, but can be disabled. A client must be
running and able to contact the feed to discover the correction. Installation
requires the user's consent; unsaved work and Windows elevation are handled by
the existing update flow. A declined or disabled update cannot force migration.

The bilingual agreement is maintained identically in LICENSE and
installer/LICENSE.txt. Matching accessible HTML pages are in help/ and help/ar/.
Existing noncommercial restrictions, third-party rights, prior MIT rights and the
English-reference clause remain. Added explanations describe actual OCR, online
correction and update behaviour; they do not promise error-free recognition.


## Long-document processing

There is no fixed PDF page-count or overall recognition-duration cutoff. The
old five-minute timeout applied to a single Paddle request, not an entire PDF;
it has been removed now that the user can explicitly cancel an active request.
Worker exit/protocol errors still produce actionable errors and retain already
completed editor pages. Cancellation interrupts PDF tools, preprocessing and
both OCR engines. PDF inspection/rendering is page-scoped, with temporary raster
files removed as each page finishes.

The bundled PP-OCRv5 mobile detector's own inference configuration uses a
960-pixel long edge. The worker now explicitly sets text_det_limit_side_len=960
and text_det_limit_type=max, avoiding the generic pipeline's minimum-side
configuration retaining full 300-DPI page dimensions for detection. Recognition
continues on original-resolution crops; existing Arabic/English/French content,
confidence geometry and hybrid-PDF regression checks must pass with this setting.

The four-minute cutoff in the initial owner-PDF diagnostic was a test-only
cancellation guard. The subsequent full 59-page audit has no such cutoff. This
must not be described as an application limit or a completed whole-file test
until its final completion record is observed.


## Editable reading during recognition

The editor now remains editable for local OCR. New pages append while retaining
user changes, including typing before the first result. The page model tracks
insertions/deletions and restores header positions after whole-document cut and
paste. Previous/Next buttons follow the editor in the Tab sequence, and F7 goes
to the latest completed page. Following new pages defaults on per the owner's
request and can be turned off in Accessibility settings; selections and recent
editing temporarily protect the reading position. API correction still protects
its pending text snapshot separately. Plain page headings replace decorative
dash separators in editor output; legacy non-editor output keeps its existing
format. Help and shortcut pages describe the new behaviour.

Publication authorization: On 26 October 2026 the owner requested completion of
final checks and publication. Retain the displayed 1.1.0 version, use internal
build/tag 1.1.0.1 for update discovery, and retain consent before installation.
