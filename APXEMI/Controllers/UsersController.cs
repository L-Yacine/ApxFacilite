using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Gestion des comptes réservée au Propriétaire (PRD §5) — appliqué côté serveur,
// un Vendeur ne peut jamais créer de compte ni s'attribuer le rôle Propriétaire.
[Authorize(Roles = nameof(UserRole.Owner))]
public class UsersController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Role) // Owner (0) avant Seller (1)
            .ThenBy(u => u.Name)
            .ToListAsync();

        var namesById = users.ToDictionary(u => u.Id, u => u.Name);

        var model = users.Select(u => new UserListItemViewModel
        {
            Id = u.Id,
            Name = u.Name,
            Username = u.Username,
            Role = u.Role,
            IsActive = u.IsActive,
            CreatedAtUtc = u.CreatedAtUtc,
            CreatedByName = u.CreatedByUserId is { } creatorId && namesById.TryGetValue(creatorId, out var n)
                ? n
                : null,
            LastLoginAtUtc = u.LastLoginAtUtc
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateUserViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        model.Username = model.Username.Trim();
        model.Name = model.Name.Trim();

        if (await db.Users.AnyAsync(u => u.Username == model.Username))
            ModelState.AddModelError(nameof(model.Username), "Ce nom d'utilisateur est déjà utilisé.");

        if (!ModelState.IsValid)
            return View(model);

        var user = new User
        {
            Name = model.Name,
            Username = model.Username,
            Role = model.Role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, model.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Le compte « {user.Name} » a été créé avec succès.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        return View(new EditUserViewModel
        {
            Id = user.Id,
            Name = user.Name,
            Username = user.Username,
            Role = user.Role,
            IsActive = user.IsActive,
            IsSelf = user.Id == currentUser.UserId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditUserViewModel model)
    {
        if (id != model.Id)
            return BadRequest();

        var user = await db.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        model.Username = model.Username.Trim();
        model.Name = model.Name.Trim();
        model.IsSelf = user.Id == currentUser.UserId;
        model.IsActive = user.IsActive;

        // Un Propriétaire ne peut pas changer son propre rôle.
        var newRole = model.IsSelf ? user.Role : model.Role;

        if (await db.Users.AnyAsync(u => u.Username == model.Username && u.Id != id))
            ModelState.AddModelError(nameof(model.Username), "Ce nom d'utilisateur est déjà utilisé.");

        if (user.Role == UserRole.Owner && newRole != UserRole.Owner &&
            await CountActiveOwnersAsync() <= 1)
        {
            ModelState.AddModelError(string.Empty,
                "Impossible : c'est le dernier compte Propriétaire actif.");
        }

        if (!ModelState.IsValid)
            return View(model);

        user.Name = model.Name;
        user.Username = model.Username;
        user.Role = newRole;
        user.UpdatedAtUtc = DateTime.UtcNow;
        user.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = "Modifications enregistrées avec succès.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var user = await db.Users.FindAsync(model.Id);
        if (user is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Le mot de passe doit contenir au moins 8 caractères et les deux saisies doivent correspondre.";
            return RedirectToAction(nameof(Edit), new { id = model.Id });
        }

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, model.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;
        user.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Le mot de passe de « {user.Name} » a été réinitialisé.";
        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        if (user.Id == currentUser.UserId)
        {
            TempData["Error"] = "Vous ne pouvez pas désactiver votre propre compte.";
            return RedirectToAction(nameof(Index));
        }

        if (user.IsActive && user.Role == UserRole.Owner && await CountActiveOwnersAsync() <= 1)
        {
            TempData["Error"] = "Impossible : c'est le dernier compte Propriétaire actif.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;
        user.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = user.IsActive
            ? $"Le compte « {user.Name} » a été réactivé."
            : $"Le compte « {user.Name} » a été désactivé ; l'accès est révoqué immédiatement.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<int> CountActiveOwnersAsync() =>
        await db.Users.CountAsync(u => u.Role == UserRole.Owner && u.IsActive);
}
