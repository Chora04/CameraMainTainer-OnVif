using System.Globalization;
using CameraMaintainer.Web.Localization;

namespace CameraMaintainer.Tests;

public sealed class CameraErrorMessagesTests
{
    private const string Timeout = "Превышено время ожидания ответа камеры. Проверка не завершилась за отведённое время.";

    [Theory]
    [InlineData("timeout: A task was canceled.")]
    [InlineData(" TIMEOUT: The operation was canceled. ")]
    [InlineData("A task was canceled.")]
    [InlineData("A task was cancelled.")]
    public void LegacyTimeoutsUseTheTranslationResource(string message)
    {
        Assert.Equal(Timeout, CameraErrorMessages.Translate(message));
        Assert.Equal(Timeout, CameraErrorMessages.Translate(message, CultureInfo.GetCultureInfo("uz")));
        Assert.Equal(Timeout, CameraErrorMessages.GetTranslations()["CameraCheckTimeout"]);
    }

    [Theory]
    [InlineData("connection_refused: No connection could be made.")]
    [InlineData("Проверку отменил оператор")]
    [InlineData("Проверка завершена: timeout был устранён")]
    [InlineData(Timeout)]
    [InlineData("")]
    public void UnrelatedAndAlreadyTranslatedMessagesArePreserved(string message)
    {
        Assert.Equal(message, CameraErrorMessages.Translate(message));
    }
}
