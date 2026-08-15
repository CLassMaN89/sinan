using PacsCdTransfer.Core.Models;

namespace PacsCdTransfer.Core.Services;

public sealed class AuthService
{
    public UserAccount? TryLogin(AppSettings settings, string username, string password)
    {
        var user = settings.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (user is null) return null;
        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }
}
