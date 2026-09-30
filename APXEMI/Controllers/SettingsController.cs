using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Compte du magasin réservé au Propriétaire (PRD §5) — appliqué côté serveur.
// La valeur enregistrée sera reprise automatiquement sur chaque ligne de la
// fiche de prélèvement (issue #9), sans ressaisie.
[Authorize(Roles = nameof(UserRole.Owner))]
public class SettingsController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await db.StoreSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StoreSettings.SingletonId);

        var model = new StoreSettingsViewModel();
        if (settings is not null)
        {
            model.StoreAccountNumber = settings.StoreAccountNumber;
            model.StoreAccountKey = settings.StoreAccountKey;
            model.IsConfigured = true;
            model.UpdatedAtUtc = settings.UpdatedAtUtc ?? settings.CreatedAtUtc;

            var updatedById = settings.UpdatedByUserId ?? settings.CreatedByUserId;
            if (updatedById is { } uid)
                model.UpdatedByName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == uid)
                    .Select(u => u.Name)
                    .FirstOrDefaultAsync();
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(StoreSettingsViewModel model)
    {
        model.StoreAccountNumber = model.StoreAccountNumber.Trim();
        model.StoreAccountKey = model.StoreAccountKey.Trim();

        if (!ModelState.IsValid)
            return View(model);

        var settings = await db.StoreSettings
            .FirstOrDefaultAsync(s => s.Id == StoreSettings.SingletonId);

        if (settings is null)
        {
            settings = new StoreSettings
            {
                Id = StoreSettings.SingletonId,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser.UserId
            };
            db.StoreSettings.Add(settings);
        }
        else
        {
            settings.UpdatedAtUtc = DateTime.UtcNow;
            settings.UpdatedByUserId = currentUser.UserId;
        }

        settings.StoreAccountNumber = model.StoreAccountNumber;
        settings.StoreAccountKey = model.StoreAccountKey;
        await db.SaveChangesAsync();

        TempData["Success"] = "Le compte du magasin a été enregistré. Il sera utilisé sur la prochaine fiche de prélèvement.";
        return RedirectToAction(nameof(Index));
    }
}
