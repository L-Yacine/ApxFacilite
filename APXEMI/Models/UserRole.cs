namespace APXEMI.Models;

public enum UserRole
{
    Owner,
    Seller
}

public static class UserRoleExtensions
{
    public static string ToFrench(this UserRole role) => role switch
    {
        UserRole.Owner => "Propriétaire",
        UserRole.Seller => "Vendeur",
        _ => role.ToString()
    };
}
