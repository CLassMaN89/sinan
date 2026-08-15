namespace PacsCdTransfer.Core.Models;

public sealed class UserAccount
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public bool CanTransferCd { get; set; } = true;
    public bool CanQueryRetrieve { get; set; } = true;
}
