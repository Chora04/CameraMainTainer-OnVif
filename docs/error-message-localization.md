# Camera Error Translations

Camera error messages are translated for display; stored fault codes and raw
exception messages remain unchanged for diagnostics and historical records.

The default Russian catalog is
`src/CameraMaintainer.Web/Resources/CameraErrorMessages.resx`.
`CameraCheckTimeout` covers legacy `timeout:` messages and the .NET task-canceled
message. Unrecognized messages are preserved.

To add a language, add `CameraErrorMessages.<culture>.resx` with the same keys,
for example `CameraErrorMessages.uz.resx`. Pass the selected account culture to
both `CameraErrorMessages.Translate` and `GetTranslations`; the latter supplies
the browser catalog in `_Layout.cshtml`. Until account language selection is
implemented, Russian is the default. Missing translations fall back to Russian.
