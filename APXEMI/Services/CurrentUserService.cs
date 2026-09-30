using System.Security.Claims;
using APXEMI.Models;

namespace APXEMI.Services;

public interface ICurrentUserService
{
    int? UserId { get; }
    string? Name { get; }
    UserRole? Role { get; }
    bool IsOwner { get; }
}

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Name => User?.FindFirstValue(ClaimTypes.Name);

    public UserRole? Role =>
        Enum.TryParse<UserRole>(User?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public bool IsOwner => Role == Models.UserRole.Owner;
}
