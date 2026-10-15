using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    private static readonly (string Hash, string Salt) MissingUserPassword =
        PasswordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    private readonly AppDbContext _db;
    private readonly AlertBoardRepository _alertBoardRepository;
    private readonly AccountSessionStore _sessions;
    private readonly bool _allowSelfRegistration;

    public AccountsController(
        AppDbContext db,
        AlertBoardRepository alertBoardRepository,
        AccountSessionStore sessions,
        IConfiguration configuration)
    {
        _db = db;
        _alertBoardRepository = alertBoardRepository;
        _sessions = sessions;
        _allowSelfRegistration = configuration.GetValue("Accounts:AllowSelfRegistration", false);
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] LoginDto dto)
    {
        var username = dto.Username.Trim();
        var user = await _db.AppUsers.SingleOrDefaultAsync(x => x.Username == username);
        var passwordValid = user == null
            ? PasswordHasher.Verify(dto.Password, MissingUserPassword.Hash, MissingUserPassword.Salt)
            : PasswordHasher.Verify(dto.Password, user.PasswordHash, user.PasswordSalt);
        if (user == null || !user.IsActive || !passwordValid)
        {
            return Unauthorized(new { message = "Неверное имя пользователя или пароль" });
        }

        var token = _sessions.Create(user.Id, TimeSpan.FromHours(12));
        SetSessionCookie(token);
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            account = ToDto(user)
        });
    }

    [HttpPost("logout")]
    public ActionResult Logout()
    {
        var token = ReadToken();
        if (!string.IsNullOrEmpty(token))
            _sessions.Remove(token);
        Response.Cookies.Delete(SessionTokenReader.CookieName);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("register")]
    public async Task<ActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!_allowSelfRegistration)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Самостоятельная регистрация отключена" });

        var username = dto.Username.Trim();
        var validation = Validate(username, dto.Password, true);
        if (validation != null)
            return BadRequest(new { message = validation });
        if (await _db.AppUsers.AnyAsync(x => x.Username == username))
            return Conflict(new { message = "Пользователь с таким именем уже существует" });

        var password = PasswordHasher.Hash(dto.Password);
        var user = new AppUser
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? username : dto.DisplayName.Trim(),
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            Role = "Viewer",
            CanViewDashboard = true,
            CanViewCameras = true,
            CanViewAlerts = false,
            CanViewAlertHistory = false,
            CanViewMaintenance = false,
            CanRunChecks = false,
            CanManageCameras = false,
            CanManageUsers = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Me), new { id = user.Id }, new { message = "Аккаунт создан" });
    }

    [HttpGet("me")]
    public async Task<ActionResult> Me()
    {
        var current = await CurrentUserAsync();
        return current == null
            ? Unauthorized(new { message = "Сеанс недействителен или истёк" })
            : Ok(ToDto(current));
    }

    [EnableRateLimiting("authentication")]
    [HttpPut("me/password")]
    public async Task<ActionResult> ChangeOwnPassword([FromBody] ChangePasswordDto dto)
    {
        var current = await CurrentUserAsync();
        if (current == null)
            return Unauthorized(new { message = "Сеанс недействителен или истёк" });

        if (!PasswordHasher.Verify(dto.CurrentPassword, current.PasswordHash, current.PasswordSalt))
            return BadRequest(new { message = "Текущий пароль указан неверно" });

        var validation = Validate(current.Username, dto.NewPassword, true);
        if (validation != null)
            return BadRequest(new { message = validation });
        if (PasswordHasher.Verify(dto.NewPassword, current.PasswordHash, current.PasswordSalt))
            return BadRequest(new { message = "Новый пароль должен отличаться от текущего" });

        var password = PasswordHasher.Hash(dto.NewPassword);
        current.PasswordHash = password.Hash;
        current.PasswordSalt = password.Salt;
        current.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _sessions.RemoveUser(current.Id);
        var token = _sessions.Create(current.Id, TimeSpan.FromHours(12));
        SetSessionCookie(token);
        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = AccessPolicies.ManageUsers)]
    public async Task<ActionResult> GetAll()
    {
        var current = await CurrentUserAsync();
        if (current == null)
            return Unauthorized(new { message = "Сеанс недействителен или истёк" });
        if (!IsAdministrator(current))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ запрещён" });

        var users = await _db.AppUsers
            .AsNoTracking()
            .OrderBy(x => x.Username)
            .ToListAsync();
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Policy = AccessPolicies.ManageUsers)]
    public async Task<ActionResult> Create([FromBody] AccountWriteDto dto)
    {
        var current = await CurrentUserAsync();
        if (current == null)
            return Unauthorized(new { message = "Сеанс недействителен или истёк" });
        if (!IsAdministrator(current))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ запрещён" });

        var username = dto.Username.Trim();
        var validation = Validate(username, dto.Password, true);
        if (validation != null)
            return BadRequest(new { message = validation });
        if (await _db.AppUsers.AnyAsync(x => x.Username == username))
            return Conflict(new { message = "Пользователь с таким именем уже существует" });

        var password = PasswordHasher.Hash(dto.Password!);
        var user = new AppUser
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? username : dto.DisplayName.Trim(),
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            CreatedAt = DateTime.UtcNow
        };
        Apply(user, dto, await NormalizeBoardColumnKeysAsync(dto.AlertBoardColumnKeys));
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = user.Id }, ToDto(user));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AccessPolicies.ManageUsers)]
    public async Task<ActionResult> Update(int id, [FromBody] AccountWriteDto dto)
    {
        var current = await CurrentUserAsync();
        if (current == null)
            return Unauthorized(new { message = "Сеанс недействителен или истёк" });
        if (!IsAdministrator(current))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ запрещён" });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound(new { message = "Аккаунт не найден" });
        if (user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) &&
            (dto.Role != "Admin" || !dto.IsActive || !dto.CanViewDashboard || !dto.CanViewCameras ||
             !dto.CanViewAlerts || !dto.CanViewAlertHistory || !dto.CanViewMaintenance ||
             !dto.CanRunChecks || !dto.CanManageCameras || !dto.CanManageUsers))
        {
            return BadRequest(new { message = "Встроенный администратор должен оставаться активным и иметь полный доступ" });
        }
        if (current.Id == user.Id &&
            (!dto.IsActive || !dto.CanManageUsers ||
             !string.Equals(dto.Role, "Admin", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Нельзя заблокировать свой аккаунт или отозвать у себя право управления доступами" });

        var validation = Validate(user.Username, dto.Password, false);
        if (validation != null)
            return BadRequest(new { message = validation });

        user.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName)
            ? user.Username
            : dto.DisplayName.Trim();
        if (!string.IsNullOrEmpty(dto.Password))
        {
            var password = PasswordHasher.Hash(dto.Password);
            user.PasswordHash = password.Hash;
            user.PasswordSalt = password.Salt;
        }
        Apply(user, dto, await NormalizeBoardColumnKeysAsync(dto.AlertBoardColumnKeys));
        await _db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AccessPolicies.ManageUsers)]
    public async Task<ActionResult> Delete(int id)
    {
        var current = await CurrentUserAsync();
        if (current == null)
            return Unauthorized(new { message = "Сеанс недействителен или истёк" });
        if (!IsAdministrator(current))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Доступ запрещён" });

        var user = await _db.AppUsers.FindAsync(id);
        if (user == null)
            return NotFound(new { message = "Аккаунт не найден" });
        if (user.Id == current.Id || user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Текущий или встроенный аккаунт администратора удалить нельзя" });

        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();
        _sessions.RemoveUser(id);
        return NoContent();
    }

    private async Task<AppUser?> CurrentUserAsync()
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetUserId(token, out var userId))
            return null;
        return await _db.AppUsers.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive);
    }

    private string? ReadToken()
    {
        return SessionTokenReader.Read(Request);
    }

    private void SetSessionCookie(string token)
    {
        Response.Cookies.Append(SessionTokenReader.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddHours(12)
        });
    }

    private static string? Validate(string username, string? password, bool passwordRequired)
    {
        if (username.Length is < 3 or > 64 ||
            username.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
        {
            return "Имя пользователя должно содержать от 3 до 64 букв, цифр или символов '.', '_' и '-'";
        }
        if ((passwordRequired || !string.IsNullOrEmpty(password)) && (password?.Length ?? 0) < 8)
            return "Пароль должен содержать не менее 8 символов";
        return null;
    }

    private async Task<List<string>?> NormalizeBoardColumnKeysAsync(List<string>? requestedKeys)
    {
        if (requestedKeys == null)
            return null;

        var knownKeys = (await _alertBoardRepository.GetColumnsAsync())
            .Select(column => column.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requestedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key) && knownKeys.Contains(key.Trim()))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Apply(AppUser user, AccountWriteDto dto, List<string>? alertBoardColumnKeys)
    {
        user.Role = dto.Role switch
        {
            "Admin" => "Admin",
            "Technician" => "Technician",
            "Operator" => "Operator",
            _ => "Viewer"
        };
        user.CanViewDashboard = dto.CanViewDashboard;
        user.CanViewCameras = dto.CanViewCameras;
        user.CanViewAlerts = dto.CanViewAlerts;
        user.CanViewAlertHistory = dto.CanViewAlerts && dto.CanViewAlertHistory;
        user.CanViewMaintenance = dto.CanViewCameras && dto.CanViewMaintenance;
        user.CanRunChecks = dto.CanRunChecks && (dto.CanViewCameras || dto.CanViewAlerts);
        user.CanManageCameras = dto.CanViewCameras && dto.CanManageCameras;
        user.CanManageUsers = dto.Role == "Admin" && dto.CanManageUsers;
        user.AlertBoardColumnKeys = user.Role == "Admin"
            ? null
            : AlertBoardRoleVisibility.SerializeAccountColumnKeys(alertBoardColumnKeys);
        user.IsActive = dto.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
    }

    private static bool IsAdministrator(AppUser user) =>
        user.CanManageUsers &&
        string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    private static AccountDto ToDto(AppUser user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Role = user.Role,
        CanViewDashboard = user.CanViewDashboard,
        CanViewCameras = user.CanViewCameras,
        CanViewAlerts = user.CanViewAlerts,
        CanViewAlertHistory = user.CanViewAlerts && user.CanViewAlertHistory,
        CanViewMaintenance = user.CanViewCameras && user.CanViewMaintenance,
        CanRunChecks = user.CanRunChecks && (user.CanViewCameras || user.CanViewAlerts),
        CanManageCameras = user.CanViewCameras && user.CanManageCameras,
        CanManageUsers = user.CanManageUsers &&
            string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase),
        AlertBoardColumnKeys = user.AlertBoardColumnKeys == null
            ? null
            : AlertBoardRoleVisibility.ParseAccountColumnKeys(user.AlertBoardColumnKeys).ToList(),
        IsActive = user.IsActive,
        LastLoginAt = user.LastLoginAt
    };
}

public sealed class LoginDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterDto
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class AccountWriteDto
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string Role { get; set; } = "Viewer";
    public bool CanViewDashboard { get; set; } = true;
    public bool CanViewCameras { get; set; } = true;
    public bool CanViewAlerts { get; set; }
    public bool CanViewAlertHistory { get; set; }
    public bool CanViewMaintenance { get; set; }
    public bool CanRunChecks { get; set; }
    public bool CanManageCameras { get; set; }
    public bool CanManageUsers { get; set; }
    public List<string>? AlertBoardColumnKeys { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AccountDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool CanViewDashboard { get; set; }
    public bool CanViewCameras { get; set; }
    public bool CanViewAlerts { get; set; }
    public bool CanViewAlertHistory { get; set; }
    public bool CanViewMaintenance { get; set; }
    public bool CanRunChecks { get; set; }
    public bool CanManageCameras { get; set; }
    public bool CanManageUsers { get; set; }
    public List<string>? AlertBoardColumnKeys { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
