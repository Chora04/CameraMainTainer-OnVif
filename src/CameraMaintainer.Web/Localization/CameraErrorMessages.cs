using CameraMaintainer.Core.Enums;
using System.Globalization;
using System.Resources;

namespace CameraMaintainer.Web.Localization;

public static class CameraErrorMessages
{
    private static readonly ResourceManager Resources = new(
        "CameraMaintainer.Web.Resources.CameraErrorMessages", typeof(CameraErrorMessages).Assembly);
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo("ru");

    public static IReadOnlyDictionary<string, string> GetTranslations(CultureInfo? culture = null) =>
        new Dictionary<string, string>
        {
            ["CameraCheckTimeout"] = Resources.GetString("CameraCheckTimeout", culture ?? DefaultCulture)!,
            ["FailureReasonTimeout"] = Text("FailureReasonTimeout", culture),
            ["FailureReasonConnectionRefused"] = Text("FailureReasonConnectionRefused", culture),
            ["FailureReasonAuthFailed"] = Text("FailureReasonAuthFailed", culture),
            ["FailureReasonNotFound"] = Text("FailureReasonNotFound", culture),
            ["FailureReasonHttpError"] = Text("FailureReasonHttpError", culture),
            ["FailureReasonSoapFault"] = Text("FailureReasonSoapFault", culture),
            ["FailureReasonNoCredentials"] = Text("FailureReasonNoCredentials", culture),
            ["FailureReasonCredentialsInvalid"] = Text("FailureReasonCredentialsInvalid", culture),
            ["FailureReasonCircuitOpen"] = Text("FailureReasonCircuitOpen", culture),
            ["FailureReasonUnknown"] = Text("FailureReasonUnknown", culture),
            ["FailureStageNetworkProbe"] = Text("FailureStageNetworkProbe", culture),
            ["FailureStageDeviceInfo"] = Text("FailureStageDeviceInfo", culture),
            ["FailureStageProcessing"] = Text("FailureStageProcessing", culture),
            ["FailureStageFirmware"] = Text("FailureStageFirmware", culture),
            ["FailureStageMetadata"] = Text("FailureStageMetadata", culture),
            ["FailureStageStatus"] = Text("FailureStageStatus", culture),
            ["FailureStageAlerts"] = Text("FailureStageAlerts", culture),
            ["FailureStageHealth"] = Text("FailureStageHealth", culture),
            ["DiagnosisReasonLabel"] = Text("DiagnosisReasonLabel", culture),
            ["DiagnosisStageLabel"] = Text("DiagnosisStageLabel", culture),
            ["DiagnosisTimingLabel"] = Text("DiagnosisTimingLabel", culture),
            ["DiagnosisNextCheckLabel"] = Text("DiagnosisNextCheckLabel", culture),
            ["DiagnosisLegacyStage"] = Text("DiagnosisLegacyStage", culture),
            ["DiagnosisTimeMissing"] = Text("DiagnosisTimeMissing", culture),
            ["DiagnosisLegacyTimeout"] = Text("DiagnosisLegacyTimeout", culture),
            ["DiagnosisTimingWithLimit"] = Text("DiagnosisTimingWithLimit", culture),
            ["DiagnosisInternalStage"] = Text("DiagnosisInternalStage", culture),
            ["DiagnosisRecovered"] = Text("DiagnosisRecovered", culture),
            ["DiagnosisActive"] = Text("DiagnosisActive", culture)
        };

    private static string Text(string key, CultureInfo? culture) =>
        Resources.GetString(key, culture ?? DefaultCulture) ?? key;

    public static string Translate(string? message, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return message ?? string.Empty;
        var text = message.Trim();
        if (text.StartsWith("timeout:", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text.TrimEnd('.'), "A task was canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text.TrimEnd('.'), "A task was cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Resources.GetString("CameraCheckTimeout", culture ?? DefaultCulture)!;
        }

        return message;
    }

    public static string Title(AlertType type, string? source, string? description = null)
    {
        if (type == AlertType.CameraOffline &&
            !string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("Ошибка проверки камеры", StringComparison.OrdinalIgnoreCase))
        {
            var failureTitle = FailureTitle(description);
            if (failureTitle != null) return failureTitle;
        }

        if (!string.IsNullOrWhiteSpace(source) &&
            source.Any(character => character is >= '\u0400' and <= '\u04FF'))
        {
            return source;
        }

        return type switch
        {
            AlertType.CameraOffline => "Камера недоступна",
            AlertType.TemperatureHigh => "Повышенная температура камеры",
            AlertType.FirmwareOutdated => "Требуется обновление прошивки",
            AlertType.AuthenticationFailed => "Ошибка авторизации камеры",
            AlertType.ConfigurationChanged => "Изменились настройки камеры",
            AlertType.AutoRecoveryFailed => "Автоматическое восстановление не выполнено",
            AlertType.LensObstructed => "Проблема с объективом камеры",
            _ => "Ошибка камеры"
        };
    }

    private static string? FailureTitle(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var separator = description.IndexOf(':', StringComparison.Ordinal);
        var code = (separator >= 0 ? description[..separator] : description).Trim().ToLowerInvariant();
        return code switch
        {
            "timeout" => "Камера не ответила вовремя",
            "connection_refused" => "Не удалось подключиться к камере",
            "auth_failed" => "Камера отклонила логин или пароль",
            "not_found" => "ONVIF-служба камеры не найдена",
            "http_error" => "Камера вернула ошибку HTTP",
            "soap_fault" => "Камера вернула ошибку ONVIF",
            _ => null
        };
    }
}
