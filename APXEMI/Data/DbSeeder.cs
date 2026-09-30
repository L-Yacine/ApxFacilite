using APXEMI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Data;

public static class DbSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<EmiDbContext>();
        await db.Database.MigrateAsync();

        var owner = await GetOrCreateAsync(db, configuration["Seed:OwnerName"] ?? "Propriétaire",
            configuration["Seed:OwnerUsername"] ?? "proprietaire",
            configuration["Seed:OwnerPassword"] ?? "Emi@2026",
            UserRole.Owner);

        // Compte de démonstration « vendeur » (Seed:DemoData) — les deux rôles
        // sont visibles sur leurs tableaux de bord respectifs.
        User? seller = null;
        if (configuration.GetValue<bool>("Seed:DemoData", true)
            && !string.IsNullOrWhiteSpace(configuration["Seed:SellerUsername"]))
        {
            seller = await GetOrCreateAsync(db, configuration["Seed:SellerName"] ?? "Vendeur",
                configuration["Seed:SellerUsername"]!,
                configuration["Seed:SellerPassword"] ?? "Vendeur@2026",
                UserRole.Seller);
        }

        if (configuration.GetValue<bool>("Seed:DemoData", true))
            await DemoDataSeeder.SeedAsync(db, owner.Id, seller?.Id);
    }

    // Idempotent : réutilise le compte existant si le nom d'utilisateur est
    // déjà en base, ne modifie jamais un mot de passe déjà configuré.
    private static async Task<User> GetOrCreateAsync(EmiDbContext db, string name, string username,
        string password, UserRole role)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (existing is not null)
            return existing;

        var user = new User
        {
            Name = name,
            Username = username,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
