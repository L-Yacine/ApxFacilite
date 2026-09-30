using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Fiche client (PRD §7.4, §9) : recherche de client par nom, prénom,
// téléphone ou n° de compte, profil listant TOUS ses plans (passés et en
// cours), et export .xlsx d'une fiche client PAR plan. Propriétaire et
// Vendeur y ont accès (table §5). Le endpoint `Search` (AJAX) alimente aussi
// le formulaire de vente (#7) — même logique anti-doublon ici (criter #8.1).
[Authorize]
public class ClientsController(EmiDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        q = q?.Trim();
        var query = db.Clients.AsNoTracking()
            .Include(c => c.Plans)
            .AsQueryable();

        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(c => c.LastName.Contains(q)
                || c.FirstName.Contains(q) || c.AccountNumber.Contains(q)
                || (c.Phone != null && c.Phone.Contains(q)));
        }

        // Sans recherche, on ne charge qu'une fenêtre — le grand volume (PRD
        // §10 : quelques milliers de clients) se parcourt par la recherche.
        var clients = await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Take(100)
            .ToListAsync();

        var model = new ClientIndexViewModel
        {
            Q = q,
            Clients = clients.Select(c => new ClientListItemViewModel
            {
                Id = c.Id,
                FullName = c.FullName,
                AccountNumber = c.AccountNumber,
                Phone = c.Phone,
                PlanCount = c.Plans.Count,
                ActivePlanCount = c.Plans.Count(p => p.Status == InstalmentPlanStatus.Active)
            }).ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var client = await db.Clients.AsNoTracking()
            .Include(c => c.Plans)
                .ThenInclude(p => p.Lines)
                    .ThenInclude(l => l.Product)
            .Include(c => c.Plans)
                .ThenInclude(p => p.Instalments)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (client is null)
            return NotFound();

        var model = new ClientDetailsViewModel
        {
            Id = client.Id,
            FullName = client.FullName,
            AccountNumber = client.AccountNumber,
            AccountKey = client.AccountKey,
            Phone = client.Phone,
            DossierNumber = client.DossierNumber,
            CreatedAtUtc = client.CreatedAtUtc,
            CreatedByName = client.CreatedByUserId is { } cid
                ? await db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.Name).FirstOrDefaultAsync()
                : null,
            Plans = client.Plans
                .OrderByDescending(p => p.SaleDate)
                .ThenByDescending(p => p.Id)
                .Select(p => new ClientPlanListItemViewModel
                {
                    PlanId = p.Id,
                    SaleDate = p.SaleDate,
                    Status = p.Status,
                    Products = string.Join(", ", p.Lines.Select(l => $"{l.Product.Name} ×{l.Quantity}")),
                    TotalAmount = p.TotalAmount,
                    DownPayment = p.DownPayment,
                    NumberOfInstalments = p.NumberOfInstalments,
                    InstalmentAmount = p.InstalmentAmount,
                    AmountPaid = p.DownPayment + p.Instalments
                        .Where(i => i.Status == InstalmentStatus.Paid).Sum(i => i.Amount)
                }).ToList()
        };

        return View(model);
    }

    // Export de la fiche client d'UN plan (PRD §9) : la carte client du
    // magasin — bloc d'informations, produits, échéancier et signatures.
    // Payé/restant calculés par le système, jamais saisis.
    [HttpGet]
    public async Task<IActionResult> ExportFiche(int planId)
    {
        var plan = await db.InstalmentPlans.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Product)
            .Include(p => p.Instalments)
            .FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null)
            return NotFound();

        var client = plan.Client;
        var amountPaid = plan.DownPayment + plan.Instalments
            .Where(i => i.Status == InstalmentStatus.Paid).Sum(i => i.Amount);

        var data = new FicheExporter.ClientFicheData
        {
            LastName = client.LastName,
            FirstName = client.FirstName,
            AccountNumber = client.AccountNumber,
            AccountKey = client.AccountKey,
            Phone = client.Phone,
            DossierNumber = client.DossierNumber,
            FirstInstalmentDate = plan.Instalments.Count > 0
                ? plan.Instalments.Min(i => i.DueDate)
                : plan.SaleDate,
            LastInstalmentDate = plan.Instalments.Count > 0
                ? plan.Instalments.Max(i => i.DueDate)
                : plan.SaleDate,
            NumberOfInstalments = plan.NumberOfInstalments,
            InstalmentAmount = plan.InstalmentAmount,
            AmountPaid = amountPaid,
            AmountRemaining = plan.TotalAmount - amountPaid,
            Lines = plan.Lines
                .OrderBy(l => l.Product.Name)
                .Select(l => new FicheExporter.ClientFicheLine(
                    l.Product.Name, l.Quantity, l.UnitPrice))
                .ToList(),
            Instalments = plan.Instalments
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.Id)
                .Select(i => new FicheExporter.ClientFicheInstalment(
                    i.DueDate, i.Amount, i.Status.ToFrenchLabel()))
                .ToList()
        };

        var bytes = FicheExporter.BuildClientFiche(data);
        var fileName = $"Fiche client - {SanitizeFileName(client.FullName)} - vente {plan.Id}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return Json(Array.Empty<ClientSearchResult>());

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.LastName.Contains(query) || c.FirstName.Contains(query)
                || c.AccountNumber.Contains(query)
                || (c.Phone != null && c.Phone.Contains(query)))
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Take(10)
            .Select(c => new ClientSearchResult(
                c.Id, c.LastName + " " + c.FirstName, c.AccountNumber, c.Phone))
            .ToListAsync();

        return Json(clients);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(cleaned) ? "client" : cleaned;
    }
}
