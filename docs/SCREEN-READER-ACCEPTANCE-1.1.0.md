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
