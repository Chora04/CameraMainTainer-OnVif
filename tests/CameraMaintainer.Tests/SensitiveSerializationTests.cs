using System.Text.Json;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Tests;

public sealed class SensitiveSerializationTests
{
    [Fact]
    public void CameraJson_DoesNotExposeStoredCredentials()
    {
        var camera = new Camera
        {
            Id = 7,
            Name = "Test",
            Credential = new Credential
            {
                CameraId = 7,
                Username = "admin",
                PasswordEnc = "encrypted-secret",
                AuthType = "wsse"
            }
        };

        var json = JsonSerializer.Serialize(camera);

        Assert.DoesNotContain("encrypted-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordEnc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackupJson_DoesNotExposeConfigurationPayload()
    {
        var backup = new ConfigBackup
        {
            CameraId = 7,
            BackupType = "full",
            ConfigXml = "<Users>private configuration</Users>"
        };

        var json = JsonSerializer.Serialize(backup);

        Assert.DoesNotContain("private configuration", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigXml", json, StringComparison.OrdinalIgnoreCase);
    }
}
