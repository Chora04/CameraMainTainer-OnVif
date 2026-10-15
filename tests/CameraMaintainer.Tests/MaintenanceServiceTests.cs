using System.Net;
using System.Text;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class MaintenanceServiceTests
{
    [Fact]
    public async Task BackupConfigAsync_WhenEverySectionFails_DoesNotSaveEmptyBackup()
    {
        await using var fixture = await BackupFixture.CreateAsync(_ => ErrorResponse());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.BackupConfigAsync(1, "full", "test"));

        Assert.Empty(await fixture.Db.ConfigBackups.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task BackupConfigAsync_WhenOneSectionSucceeds_MarksBackupPartial()
    {
        await using var fixture = await BackupFixture.CreateAsync(async request =>
        {
            var requestBody = await request.Content!.ReadAsStringAsync();
            if (!requestBody.Contains("GetNetworkInterfaces", StringComparison.Ordinal))
                return ErrorResponse();

            return SoapResponse("""
                <tds:GetNetworkInterfacesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema">
                  <tds:NetworkInterfaces token="eth0"><tt:IPv4><tt:Config>
                    <tt:Manual><tt:Address>192.168.4.31</tt:Address><tt:PrefixLength>24</tt:PrefixLength></tt:Manual>
                    <tt:DHCP>false</tt:DHCP>
                  </tt:Config></tt:IPv4></tds:NetworkInterfaces>
                </tds:GetNetworkInterfacesResponse>
                """);
        });

        var backup = await fixture.Service.BackupConfigAsync(1, "full", "scheduler");

        Assert.Equal("full_partial", backup.BackupType);
        Assert.Equal("scheduler", backup.CreatedBy);
        Assert.Contains("<NetworkConfig>", backup.ConfigXml);
        Assert.Contains("<CollectionErrors>", backup.ConfigXml);
        Assert.Single(await fixture.Db.ConfigBackups.AsNoTracking().ToListAsync());
    }

    private static HttpResponseMessage ErrorResponse()
        => new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("camera unavailable", Encoding.UTF8, "text/plain")
        };

    private static HttpResponseMessage SoapResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>{body}</s:Body></s:Envelope>",
                Encoding.UTF8,
                "application/soap+xml")
        };

    private sealed class BackupFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HttpClient _httpClient;

        private BackupFixture(
            SqliteConnection connection,
            HttpClient httpClient,
            AppDbContext db,
            MaintenanceService service)
        {
            _connection = connection;
            _httpClient = httpClient;
            Db = db;
            Service = service;
        }

        public AppDbContext Db { get; }
        public MaintenanceService Service { get; }

        public static Task<BackupFixture> CreateAsync(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
            => CreateAsync(request => Task.FromResult(handler(request)));

        public static async Task<BackupFixture> CreateAsync(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var encryptionKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
            var encryptor = new AesCredentialEncryptor(encryptionKey);
            db.Cameras.Add(new Camera
            {
                Id = 1,
                Name = "Camera 1",
                ServiceUrl = "http://camera/onvif/device_service",
                Credential = new Credential
                {
                    Username = "admin",
                    PasswordEnc = encryptor.Encrypt("password")
                }
            });
            await db.SaveChangesAsync();

            var httpClient = new HttpClient(new StubHandler(handler));
            var service = new MaintenanceService(
                new CameraRepository(db),
                db,
                new OnvifClient(httpClient),
                encryptor,
                NullLogger<MaintenanceService>.Instance);
            return new BackupFixture(connection, httpClient, db, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _httpClient.Dispose();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _handler(request);
    }
}
