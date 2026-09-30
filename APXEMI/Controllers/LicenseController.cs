using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APXEMI.Controllers;

// Écran d'activation de la licence (hors-ligne). Accessible sans être connecté :
// c'est la toute première étape avant même la connexion. Le verrou global
// (LicenseGateFilter) redirige tout ici tant que la licence n'est pas active.
[AllowAnonymous]
public class LicenseController(LicenseService license) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var model = new LicenseViewModel
        {
            MachineId = license.MachineId,
            IsConfigured = license.IsConfigured,
            IsActivated = license.IsActivated,
            Licensee = license.Licensee
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Activate(string code)
    {
        if (license.TryActivate(code, out var error))
        {
            TempData["Success"] = "Licence activée. Bienvenue !";
            return RedirectToAction("Index", "Home");
        }

        TempData["Error"] = error;
        return RedirectToAction(nameof(Index));
    }
}
