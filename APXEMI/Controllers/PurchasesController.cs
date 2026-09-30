using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Achats fournisseurs = entrées de stock (PRD §7.2). Propriétaire et Vendeur
// peuvent saisir et consulter (table des permissions §5). L'enregistrement
// d'un achat met à jour le stock de chaque produit et écrit le mouvement
// correspondant (quantités avant/après calculées par le système) — le tout
// dans une seule transaction : jamais de stock sans mouvement, ni l'inverse.
[Authorize]
public class PurchasesController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    // Historique consultable par fournisseur, produit ou période (PRD §7.2).
    [HttpGet]
    public async Task<IActionResult> Index(int? supplierId, int? productId, DateTime? dateFrom, DateTime? dateTo)
    {
        var query = db.Purchases.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .AsQueryable();

        if (supplierId is { } sid)
            query = query.Where(p => p.SupplierId == sid);
        if (productId is { } pid)
            query = query.Where(p => p.Lines.Any(l => l.ProductId == pid));
        if (dateFrom is { } from)
            query = query.Where(p => p.InvoiceDate >= from);
        if (dateTo is { } to)
            query = query.Where(p => p.InvoiceDate <= to);

        var purchases = await query
            .OrderByDescending(p => p.InvoiceDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync();

        var creatorIds = purchases.Where(p => p.CreatedByUserId.HasValue)
            .Select(p => p.CreatedByUserId!.Value).Distinct().ToList();
        var creatorNames = await db.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var model = new PurchaseIndexViewModel
        {
            SupplierId = supplierId,
            ProductId = productId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Suppliers = await db.Suppliers.AsNoTracking()
                .OrderBy(s => s.Name)
                .Select(s => new SupplierOption(s.Id, s.Name))
                .ToListAsync(),
            Products = await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new ProductOption(p.Id, p.Name, 0))
                .ToListAsync(),
            Purchases = purchases.Select(p => new PurchaseListItemViewModel
            {
                Id = p.Id,
                InvoiceDate = p.InvoiceDate,
                SupplierName = p.Supplier.Name,
                InvoiceNumber = p.InvoiceNumber,
                PaymentMethod = p.PaymentMethod,
                CheckNumber = p.CheckNumber,
                LineCount = p.Lines.Count,
                TotalQuantity = p.Lines.Sum(l => l.Quantity),
                TotalAmount = p.Lines.Sum(l => l.Quantity * l.UnitCost),
                CreatedByName = p.CreatedByUserId is { } cid && creatorNames.TryGetValue(cid, out var n) ? n : null
            }).ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreatePurchaseViewModel
        {
            InvoiceDate = DateTime.Today,
            PaymentMethod = PurchasePaymentMethod.Cheque
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePurchaseViewModel model)
    {
        model.SupplierName = model.SupplierName.Trim();
        model.InvoiceNumber = model.InvoiceNumber.Trim();
        model.CheckNumber = string.IsNullOrWhiteSpace(model.CheckNumber) ? null : model.CheckNumber.Trim();
        model.SupplierPhone = string.IsNullOrWhiteSpace(model.SupplierPhone) ? null : model.SupplierPhone.Trim();

        // Le règlement par chèque est le seul à utiliser le numéro de chèque.
        if (model.PaymentMethod != PurchasePaymentMethod.Cheque)
            model.CheckNumber = null;

        // Ignore les lignes laissées entièrement vides dans le formulaire.
        model.Lines = model.Lines
            .Where(l => l.ProductId.HasValue || l.Quantity.HasValue || l.UnitCost.HasValue)
            .ToList();

        if (model.Lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Ajoutez au moins une ligne de produit à l'achat.");

        // Un même produit ne peut apparaître qu'une fois par achat — sinon les
        // quantités avant/après du mouvement seraient trompeuses.
        var duplicates = model.Lines.Where(l => l.ProductId.HasValue)
            .GroupBy(l => l.ProductId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        var activeProducts = await db.Products
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in model.Lines)
        {
            if (line.ProductId is { } linePid && !activeProducts.ContainsKey(linePid))
                ModelState.AddModelError(string.Empty,
                    "Un des produits choisis n'existe plus ou a été retiré du catalogue.");
        }

        foreach (var dupId in duplicates)
        {
            if (activeProducts.TryGetValue(dupId, out var dup))
                ModelState.AddModelError(string.Empty,
                    $"Le produit « {dup.Name} » apparaît sur plusieurs lignes — regroupez-le en une seule ligne.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Fournisseur : existant (suggestions) ou créé à la volée.
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Name == model.SupplierName);
        if (supplier is null)
        {
            supplier = new Supplier
            {
                Name = model.SupplierName,
                Phone = model.SupplierPhone,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser.UserId
            };
            db.Suppliers.Add(supplier);
        }

        var purchase = new Purchase
        {
            Supplier = supplier,
            InvoiceNumber = model.InvoiceNumber,
            InvoiceDate = model.InvoiceDate!.Value.Date,
            PaymentMethod = model.PaymentMethod!.Value,
            CheckNumber = model.CheckNumber,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.Purchases.Add(purchase);

        foreach (var line in model.Lines)
        {
            var product = activeProducts[line.ProductId!.Value];
            var quantity = line.Quantity!.Value;

            purchase.Lines.Add(new PurchaseLine
            {
                Product = product,
                Quantity = quantity,
                UnitCost = line.UnitCost!.Value
            });

            // Entrée de stock : avant/après calculés ici, jamais saisis.
            // On ne touche pas aux champs d'audit du produit (UpdatedAt/By) :
            // ils décrivent les modifications du *catalogue* (nom, prix…), pas
            // les variations de stock — celles-ci sont tracées par le mouvement suivant.
            var before = product.CurrentStockQuantity;
            var after = before + quantity;
            product.CurrentStockQuantity = after;

            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                MovementDate = purchase.InvoiceDate,
                QuantityBefore = before,
                Type = StockMovementType.In,
                OperatorName = supplier.Name,
                QuantityAfter = after,
                Observation = $"Achat fournisseur — facture n° {purchase.InvoiceNumber}",
                Purchase = purchase,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser.UserId
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"Achat enregistré avec succès — facture n° {purchase.InvoiceNumber}, " +
            $"{purchase.Lines.Count} ligne(s), stock mis à jour.";
        return RedirectToAction(nameof(Details), new { id = purchase.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var purchase = await db.Purchases.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (purchase is null)
            return NotFound();

        // Les quantités avant/après affichées viennent du grand livre, pas d'un
        // recalcul — l'écran reflète exactement ce qui a été écrit.
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.PurchaseId == id)
            .ToDictionaryAsync(m => m.ProductId);

        var model = new PurchaseDetailsViewModel
        {
            Id = purchase.Id,
            SupplierName = purchase.Supplier.Name,
            SupplierPhone = purchase.Supplier.Phone,
            InvoiceNumber = purchase.InvoiceNumber,
            InvoiceDate = purchase.InvoiceDate,
            PaymentMethod = purchase.PaymentMethod,
            CheckNumber = purchase.CheckNumber,
            TotalQuantity = purchase.Lines.Sum(l => l.Quantity),
            TotalAmount = purchase.Lines.Sum(l => l.Quantity * l.UnitCost),
            CreatedAtUtc = purchase.CreatedAtUtc,
            CreatedByName = purchase.CreatedByUserId is { } cid
                ? await db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.Name).FirstOrDefaultAsync()
                : null,
            Lines = purchase.Lines.Select(l =>
            {
                var hasMove = movements.TryGetValue(l.ProductId, out var move);
                return new PurchaseLineDetailViewModel
                {
                    ProductName = l.Product.Name,
                    Quantity = l.Quantity,
                    UnitCost = l.UnitCost,
                    QuantityBefore = hasMove && move is not null ? move.QuantityBefore : 0,
                    QuantityAfter = hasMove && move is not null ? move.QuantityAfter : 0
                };
            }).ToList()
        };

        return View(model);
    }

    private async Task PopulateOptionsAsync(CreatePurchaseViewModel model)
    {
        model.Suppliers = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption(s.Id, s.Name))
            .ToListAsync();
        // Seuls les produits actifs sont proposés à l'achat (critère #4.2).
        model.Products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProductOption(p.Id, p.Name, p.UnitCost))
            .ToListAsync();
    }
}
