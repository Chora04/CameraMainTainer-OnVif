# Voltera Reference Study

Reference: https://www.behance.net/gallery/246709783/Modern-Parking-App-design-Parking-Platform-Voltera

Studied the application screens and component presentation, not Behance navigation.
Local reference images are for inspection only; they are not served by Camera Maintainer.

## Observations

- The desktop composition separates a narrow navigation rail, a secondary workflow pane and the main working area.
- Repeated items use small corner radii, normal-weight typography, quiet labels and a dark action band.
- Cool gray surfaces, white items and restrained blue/green/amber statuses provide hierarchy without large shadows.
- The main visual is actual domain content. Camera Maintainer keeps real ONVIF snapshots instead of copying the parking render or adding invented charts.

## Adaptation

- Local Manrope variable font with Cyrillic support and its OFL license; no external font request at runtime.
- Expanded navigation stays icon + label; collapsed navigation uses square 44px buttons in a 68px rail.
- Error workspace: 216px processing summary, 20px gap, remaining width for stages. Each stage has a 300px minimum width, with horizontal scrolling and previous/next controls.
- Error card: camera identity and IP, full error title, muted type badge, dark start-time band. Double-click, keyboard activation, handoffs and drag-and-drop remain intact.
- Cameras: 36:64 registry/inspector split where space permits, 58:42 preview/details split. Diagnostic values are below the frame; device identity and ONVIF response remain on the right.
- Overview: inspectable 16:9 camera thumbnails, compact device information, dark action strip; errors stay in a 300px side list.
- Light and dark themes share geometry. Mobile breakpoints remain disabled as previously requested.

## Backup

`artifacts/backups/before-voltera-ui-20260914-144152.zip`

Contains the pre-change Razor, CSS, JavaScript and web tests. Does not contain the database or credentials.

## Verification

Razor render/integration tests and JS interaction tests cover the changed markup and preserved behavior. Browser screenshot verification was unavailable because Computer Use reported no connected browsers and could not create an in-app browser.
