# Changes

## 1.0.4

- Published a small Settings wording change to verify the complete in-app update flow from 1.0.3.

## 1.0.3

- Added a user-triggered GitHub Release updater in Settings.
- Update downloads require the exact Setup asset, SHA-256 verification, and matching Vanta file metadata.
- Setup can replace the installed version, retain the desktop-shortcut preference, and reopen Vanta.
- Added an enforced workflow for refreshing the website previews and logo after desktop changes.

## 1.0.2

- Bundled Paytone One and applied it across application text, controls, tooltips, and the test pad. No font installation is required. The original font license is available in Settings and included in the ZIP.
- Added eased fade-and-slide view transitions, animated window resizing, and smooth notice and dialog entrances. View motion follows Windows animation preferences, and rapid navigation settles on the latest selection.
- Increased 1-pixel outlines to 1.5 device-independent pixels while keeping the black surfaces, neutral text, blue outline accents, and actual rounded corners.
- Adjusted spacing and window sizes for the new font. Clicking behavior and saved profile format are unchanged.

## 1.0.1

- Removed the opaque rectangular backing behind rounded window borders. Main views and the test pad now have actual transparent corners and matching content clipping.
- Changed the palette to pure black and neutral gray surfaces, with white/gray text. Light blue is used only for outlines, selection borders, and focus indicators. The supplied logo is unchanged.
- Rounded cards, input fields, dropdowns, buttons, segmented selectors, notices, and tooltips; added subtle hover fades and switch motion.
- Updated the test pad with the same black theme and a rounded window, including its own close button.
- Added regression checks for corner transparency, neutral fills/text, and minimize/restore behavior. The click engine and saved profile format are unchanged.

## 1.0.0

- Initial native Windows release with rate/delay timing, global hotkeys, toggle/hold activation, click limits, variation, double clicks, cursor sequences, profiles, and a test pad.
