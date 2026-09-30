using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Fiche de stock (PRD §7.3, §9) : le grand livre d'un produit, consultable et
// exportable en .xlsx. Propriétaire et Vendeur y ont accès (table §5).
// L'écran et l'export lisent la MÊME requête sur les mouvements stockés —
// quantités avant/après jamais recalculées — donc le fichier correspond
// toujours à ce qui est affiché au moment de l'export (critère #6.3).
[Authorize]
public class StockLedgerController(EmiDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? productId)
    {
        var model = new StockLedgerIndexViewModel
        {
            Products = await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new ProductOption(p.Id, p.Name, 0))
                .ToListAsync(),
            SelectedProductId = productId
        };

        if (productId is { } pid)
        {
            var product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pid);
            if (product is not null)
            {
                model.ProductName = product.Name;
                model.CurrentStockQuantity = product.CurrentStockQuantity;
                model.Movements = await LoadMovementsAsync(pid);
            }
        }

        return View(model);
    }

    // Export .xlsx (fiche de stock du magasin : Date | Nombre | Entrée (NB |
    // Nom) | Sortie (NB | Nom) | Reste). Les mêmes mouvements stockés que
    // l'écran — jamais recalculés.
    [HttpGet]
    public async Task<IActionResult> Export(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            return NotFound();

        var movements = await LoadMovementsAsync(id);
        var rows = movements.Select(m => new FicheExporter.StockFicheRow(
            m.MovementDate,
            m.QuantityBefore,
            m.Type == StockMovementType.In,
            m.OperatorName,
            m.QuantityAfter)).ToList();

        var bytes = FicheExporter.BuildStockFiche(product.Name, rows);
        var fileName = $"Fiche de stock - {SanitizeFileName(product.Name)}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task<List<StockMovementRowViewModel>> LoadMovementsAsync(int productId)
    {
        return await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.MovementDate)
            .ThenBy(m => m.Id)
            .Select(m => new StockMovementRowViewModel
            {
                MovementDate = m.MovementDate,
                QuantityBefore = m.QuantityBefore,
                Type = m.Type,
                OperatorName = m.OperatorName,
                QuantityAfter = m.QuantityAfter,
                Observation = m.Observation
            })
            .ToListAsync();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(cleaned) ? "produit" : cleaned;
    }
}
