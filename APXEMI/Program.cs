using System.Reflection;
using System.Security.Claims;
using APXEMI.Data;
using APXEMI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var app = Program.BuildApp(args);
await Program.InitializeAsync(app);
app.Run();

public partial class Program
{
    // Point d'entrée partagé : `dotnet run` (web) et le lanceur de bureau
    // APXEMI.Desktop construisent le même hôte. Le lanceur démarre Kestrel
    // en arrière-plan au lieu d'appeler Run().
    public static WebApplication BuildApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        var mvc = builder.Services.AddControllersWithViews(o =>
        {
            o.Filters.Add<LicenseGateFilter>();
        });

        // Quand l'hôte est lancé par APXEMI.Desktop, l'assembly d'entrée est le
        // lanceur et non APXEMI : on enregistre alors explicitement APXEMI comme
        // ApplicationPart pour que contrôleurs + vues précompilées soient trouvés.
        if (Assembly.GetEntryAssembly() != typeof(Program).Assembly)
        {
            mvc.AddApplicationPart(typeof(Program).Assembly);
        }

        // Base embarquée : SQLite, un seul fichier dans %LOCALAPPDATA%\APXEMI.
        // Aucun serveur de base à installer ; sauvegarde = copier le fichier.
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "APXEMI");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "apxemi.db");
        builder.Services.AddDbContext<EmiDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = ".APXEMI.Auth";
                options.LoginPath = "/Account/Login";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;

                // Re-valide le compte à chaque requête : un compte désactivé perd
                // l'accès immédiatement, et un changement de nom/rôle est pris en
                // compte sans attendre la prochaine connexion.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var principal = context.Principal;
                    if (principal is null ||
                        !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                    {
                        context.RejectPrincipal();
                        return;
                    }

                    var db = context.HttpContext.RequestServices.GetRequiredService<EmiDbContext>();
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    if (user is null || !user.IsActive)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    if (principal.FindFirstValue(ClaimTypes.Name) != user.Name ||
                        principal.FindFirstValue(ClaimTypes.Role) != user.Role.ToString())
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                            new(ClaimTypes.Name, user.Name),
                            new(ClaimTypes.Role, user.Role.ToString())
                        };
                        context.ReplacePrincipal(new ClaimsPrincipal(
                            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
                        context.ShouldRenew = true;
                    }
                };
            });
        builder.Services.AddAuthorization();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<LegacyImportService>();
        builder.Services.AddScoped<LicenseService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // Pas de HSTS ni de redirection HTTPS : l'application est servie en
            // HTTP sur l'hôte local uniquement (app de bureau hors ligne).
        }

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseStaticFiles();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }

    // Crée la base au premier lancement et alimente le compte initial (Propriétaire).
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await DbSeeder.InitializeAsync(scope.ServiceProvider, app.Configuration);
    }
}
