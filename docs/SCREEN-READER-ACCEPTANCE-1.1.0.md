# TextBridge 1.1.0 accessibility acceptance

Status: **incomplete — do not restore stable distribution yet**.

The version stays 1.1.0. The owner authorized replacing its build after corrections and verification, rather than increasing the version. No corrected GitHub assets have been uploaded in this validation session. The existing 1.1.0 release is a preview; v1.0.2 is the stable latest release.

## Build to check

Use `artifacts/accessibility-test-1.1.0/TextBridge.exe` with its complete folder. This separate local build contains the updated help and the single Accessibility tab. The installed copy under Program Files and earlier local test windows can contain older code.

The help is English and Arabic, with matching topics, heading landmarks, local navigation, language switching and four labelled shortcut tables. Settings > Accessibility settings opens one tab with screen-reader, low-vision and sound/message groups. View > Reading appearance opens the font/contrast group in that same tab.

## Evidence so far

- All 333 application regression checks passed after the combined-tab code change. These are automated tests, not screen-reader acceptance.
- Fourteen help pages passed local link, anchor, heading, language and landmark checks. Both shortcut pages have four captioned tables.
- NVDA 2026.2 is installed and running. The user opened Speech Viewer for this session.
- Actual Speech Viewer output was observed for the Arabic editor's name/role/help, the settings dialog title, and the native Save As filename control, including its Alt+N shortcut. This is a limited transcript observation, not a complete workflow pass or an assessment of pronunciation.
- The desktop helper cannot read Speech Viewer's accessibility text because that window has higher Windows integrity. Its transcript can be inspected visually.
- The helper repeatedly routed settings-navigation input to the owner window and sometimes captured an occluding window rather than the requested app. Those attempts are not valid evidence that dialog navigation passed or failed for a physical keyboard user. No Windows security settings were changed.
- NVDA's existing log contains an add-on import error and a freeze-recovery event. These are recorded separately from TextBridge findings; this session did not change NVDA's configuration or add-ons.

## Physical-keyboard acceptance checklist

Record the reader/version, interface language, exact command, expected result and actual spoken/focus result for each item. Repeat relevant cases in English and Arabic. Use synthetic documents, not personal files.

| Check | Expected result | Status |
| --- | --- | --- |
| Open the local build | Editor name and editable role are spoken; initial focus is usable | Limited transcript observed; full pass pending |
| Alt/F10, arrows, Enter and Escape | Menus work from the keyboard; Escape closes one level; exiting returns to the prior control | Pending |
| Settings > Accessibility settings | One Accessibility tab; no separate Low vision tab; initial screen-reader option has focus | Pending |
| Three section buttons, Tab and Shift+Tab | Each button moves to the corresponding group; control name, role and value are understandable; no focus trap | Pending |
| View > Reading appearance | Same Accessibility tab, with focus on Document font and the group scrolled into view | Pending |
| Large interface text and narrow window | Font controls, contrast choices, preview, Save and Cancel remain reachable without clipped essential actions | Pending |
| Change options, then Cancel/Escape | Preferences are unchanged; focus returns to the editor or invoking control | Pending |
| Save preferences | Choices persist; document contents are not changed | Pending |
| F1, Help menu and shortcut page | Correct-language local pages open; headings, links, table captions and row/column headers are usable with the reader | Pending |
| Recognize Arabic, English and French samples | Progress/completion and errors can be reviewed; recognized text is readable; Arabic and numerals have usable order | Pending |
| F4 / Shift+F4 | Paddle review selects the appropriate word or Arabic line and gives understandable feedback; Windows confidence limitations are explained | Pending |
| Shift+F6 / F6 | Current status can be read with the bar hidden or visible; returning to the editor preserves the selection | Pending |
| Save, Word export, find/replace, missing-runtime error | Dialogs, validation and return focus work; failed or cancelled operations preserve work | Pending |
| JAWS / Narrator smoke check | Main editor, Help, Accessibility settings and a recognition status are understandable | Not run |

## Publication decision

Fix reproducible application failures and rerun affected cases. Only after the required screen-reader cases are accepted should the corrected 1.1.0 installer, portable build, source, matching tag and checksums replace the preview assets. Preserve the original build's provenance. Do not report pending cases as passed.

## Owner's physical-keyboard feedback

The owner answered “Works as expected” for the local combined Accessibility tab, section buttons, Tab/Shift+Tab, Save/Cancel and return focus, and F1/shortcut-page heading and table navigation. This accepts those tested controls and help flows only. Subsequent incremental-recognition and announcement-frequency changes need a new focused NVDA check. Automated PDF and cadence tests do not establish that speech is heard correctly.


## Correction build 1: focused owner check (pending)

Candidate: `D:\AOS\artifacts\TextBridge-1.1.0-test\TextBridge.exe`.
Displayed/product version 1.1.0; file/assembly version 1.1.0.1.

1. With NVDA running, set Accessibility settings > Screen reader and keyboard >
   Progress announcement frequency to 5 seconds, with progress announcements enabled.
2. Recognize a large multipage PDF. Confirm meaningful announcements continue on
   slow pages and use the document's actual "page ... of ..." count and percentage.
3. Confirm completed pages appear before the whole document finishes, with page
   headings. Read and select earlier text as later pages arrive: focus, selection
   and scroll position should remain usable. Shift+F6 should report current status.
4. Try the every-completed-page option on another run, and confirm the selected
   frequency persists. Copy/save available text during recognition; verify editing
   becomes available when recognition finishes.
5. Report any missing, repeated, interrupted or misleading speech, text duplication,
   page numbering problem, focus movement or error. Publication waits for feedback.

A single image is one engine result and appears when that page finishes. Batch
conversion reports file/page context and writes separate files, rather than
streaming into the editor. These are intentional, documented behaviours.


## Owner-reported failure and follow-up fix (26 October 2026)

The owner reported no progress speech or incremental text, and closing was blocked
by a wait-for-recognition message. The previous progress build is NOT accepted.

Follow-up candidate: `D:\AOS\artifacts\TextBridge-1.1.0-progress-fix\TextBridge.exe`.
The visible progress region is now a named polite live region, with both an
explicit live-region event and a UIA notification; it no longer routes timed
messages through the editor when the status bar is hidden. Real NVDA speech
still requires owner verification. Recognition runs outside the UI dispatcher;
PDF extraction and image/layout inspection are bounded to the current page.

Escape outside a menu asks Yes/No/Cancel to stop local or batch recognition.
Yes cancels the PDF/preprocessing/Paddle/Windows work; completed editor pages or
previously exported batch files remain. No/Cancel continues. Exit uses the same
confirmation, waits for cancellation cleanup, then offers the usual unsaved-text
choice before closing. Default is Cancel. Escape in a menu still closes a menu
level first. Cancellation is not reported as an OCR error.

Retest with NVDA and a large PDF: timed announcements with status bar hidden,
actual page counts, early completed-page insertion and stable reading position;
Escape > No, Escape > Cancel, Escape > Yes; recognize again; exit > No/Cancel,
then exit > Yes and save/cancel the unsaved-text prompt. Acceptance is pending.


Follow-up automated evidence: all 380 checks passed, including the actual WPF
handler's input dispatch, early page insertion, restored editing/busy state,
active Paddle cancellation and restart, and native/hybrid PDF regressions.
The owner's 59-page PDF was also processed locally with Arabic/English/French
Paddle settings: page 1 delivered at 140.5 seconds, page 2 at 183.5 seconds.
The audit's four-minute limit cancelled during page 3 at 240 seconds. This
verifies partial page delivery and in-flight cancellation on that file, not
recognition of all 59 pages or audible NVDA speech. The three-page audit target
was not reached within its time limit. No document text was written to the log.


## Complete long-document verification (26 October 2026)

Candidate: `D:\AOS\artifacts\TextBridge-1.1.0-long-doc-test\TextBridge.exe`.
All 380 functional regression checks passed with the optimized detector and
cancellable, uninterrupted Paddle requests. A separate 212-page native PDF test
passed four checks: all pages delivered in order, actual totals/100 percent,
streamed text retained exactly once, and no full-document rasterization.

The owner's complete 59-page PDF finished in 880.9 seconds (about 14 minutes
41 seconds) with Arabic/English/French Paddle settings. All page-completion events
and all final page headings were verified sequentially from 1 through 59, with
21,265 final characters. The private result remains local in artifacts; document
text is not in the diagnostic log. This validates full-file processing and page
stream delivery, not transcription accuracy or audible NVDA announcements.

The old publish-folder app had an empty editor and refused normal exit while
busy. Under the owner's explicit request to close test copies, that stale app
and its exact Paddle worker were stopped. After completion, process and desktop
checks found no remaining TextBridge windows or OCR test workers. A clean manual
NVDA run remains required before publication. Test every-five-second progress,
page-by-page insertion without selection jumps, Escape Yes/No/Cancel, and exit
Yes/No/Cancel followed by the unsaved-text prompt.


## Editable page navigation correction (26 October 2026)

This section supersedes the earlier read-only-during-OCR behaviour and automatic
reading-position preservation. The owner confirmed five-second progress speech,
then reported that NVDA was reading the first page heading while recognition had
advanced. Live inspection showed pages 1–10 already present in the editor.

The owner requested automatic following, ordinary arrow-key reading, Previous /
Next buttons in the Tab sequence, and editing, selection, cut/copy/paste/delete
while recognition continues. The corrected build implements these behaviours.
F7 also reaches the newest completed page. Automatic following pauses while a
selection is active or text has just been edited, so arriving OCR does not move
a selection being cut or pasted. New chunks append; they do not replace edits.
Page anchors track edits and recover after whole-document cut/paste. Page
headings use plain text instead of spoken dash decoration. The editor's reading
caret remains available, and the current reading page is displayed separately
from recognition progress. A local PDF/image filename passed at startup opens
that file; this also supports Windows Open with and reliable desktop testing.

Focused checks cover active editing in the actual WPF recognition handler,
retaining edits after cancellation, typing before the first page, page navigation
after edits and whole-document cut/paste, following preferences, and no reading
page zero. Complete regression results are recorded in artifacts.

Live desktop observations on the owner's 232-page book in the editable-pages
test build: all 232 pages became available and the reading label followed through
to page 232; progress speech and an Arabic body line appeared in NVDA Speech
Viewer. Tab visibly focused Previous page and then Next page. A test insertion
was visible in the editor, and pages 1 and 200 remained in the accessible value.
The early contents pages retained the Arabic native text layer in this run.
These observations do not establish word-perfect transcription or every
clipboard/navigation command under NVDA.

The helper's focus/activity reports were inconsistent; a temporary dotnet-hosted
reading preview also encountered an app-approval timeout and closed itself.
Keyboard/clipboard input was paused after repeated activity warnings, pending
confirmation that the laptop remained idle. The completed editable-pages test
window was left open with a temporary NVDA EDIT CHECK insertion. Do not claim
all copies were closed or the physical cut/copy/paste/delete checks completed.
The source PDFs were not saved over. The final test build is separate from that
open test window. Publication still requires the owner's final NVDA acceptance.


Final automated result for this correction: all 397 regression checks passed;
the focused progress/editing set has 36 checks and the actual-handler editable
recognition/cancellation set has four. Source build has no warnings or errors.
The final candidate is `D:\AOS\artifacts\TextBridge-1.1.0-final-test\TextBridge.exe`.
The open desktop test window uses the earlier editable-pages-test folder; do
not overwrite or silently close it while the idle/readiness question is pending.
