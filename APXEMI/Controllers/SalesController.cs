using System.Text.RegularExpressions;
using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Ventes = plans de mensualités (PRD §7.5, §8). Propriétaire et Vendeur
// peuvent vendre (table des permissions §5). Chaque vente est SON propre plan,
// indépendant des autres plans du client (§6). À l'enregistrement, dans une
// seule transaction : création du client si nouveau, déduction du stock de
// chaque produit + écriture de son mouvement, création du plan, de ses lignes
// et du calendrier des mensualités — jamais de stock sans mouvement, ni de
// plan sans son calendrier.
[Authorize]
public class SalesController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    private static readonly Regex AccountNumberRegex = new(@"^\d{5,20}$");
    private static readonly Regex AccountKeyRegex = new(@"^\d{1,2}$");
    private static readonly Regex PhoneRegex = new(@"^0\d{9}$");

    [HttpGet]
    public async Task<IActionResult> Index(string? search)
    {
        search = search?.Trim();
        var query = db.InstalmentPlans.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Lines)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var term = search;
            query = query.Where(p => p.Client.LastName.Contains(term)
                || p.Client.FirstName.Contains(term) || p.Client.AccountNumber.Contains(term));
        }

        var plans = await query
            .OrderByDescending(p => p.SaleDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync();

        var creatorIds = plans.Where(p => p.CreatedByUserId.HasValue)
            .Select(p => p.CreatedByUserId!.Value).Distinct().ToList();
        var creatorNames = await db.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var model = new SaleIndexViewModel
        {
            Search = search,
            Sales = plans.Select(p => new SaleListItemViewModel
            {
                Id = p.Id,
                SaleDate = p.SaleDate,
                ClientFullName = p.Client.FullName,
                ClientAccountNumber = p.Client.AccountNumber,
                LineCount = p.Lines.Count,
                TotalAmount = p.TotalAmount,
                DownPayment = p.DownPayment,
                Status = p.Status,
                CreatedByName = p.CreatedByUserId is { } cid && creatorNames.TryGetValue(cid, out var n) ? n : null
            }).ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateSaleViewModel
        {
            SaleDate = DateTime.Today,
            DownPayment = 0
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSaleViewModel model)
    {
        model.ClientSearch = model.ClientSearch?.Trim();
        model.AccountNumber = model.AccountNumber?.Trim();
        model.AccountKey = model.AccountKey?.Trim();
        model.LastName = model.LastName?.Trim();
        model.FirstName = model.FirstName?.Trim();
        model.Phone = string.IsNullOrWhiteSpace(model.Phone)
            ? null : model.Phone.Replace(" ", string.Empty).Trim();
        model.DossierNumber = string.IsNullOrWhiteSpace(model.DossierNumber)
            ? null : model.DossierNumber.Trim();

        // Ignore les lignes laissées entièrement vides dans le formulaire.
        model.Lines = model.Lines
            .Where(l => l.ProductId.HasValue || l.Quantity.HasValue || l.UnitPrice.HasValue)
            .ToList();

        await ValidateAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Client : existant (recherche) ou créé à la volée avec son compte.
        Client? client = null;
        if (model.ClientId is { } clientId)
        {
            client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        }

        if (client is null)
        {
            client = new Client
            {
                AccountNumber = model.AccountNumber!,
                AccountKey = model.AccountKey!,
                LastName = model.LastName!,
                FirstName = model.FirstName!,
                Phone = model.Phone,
                DossierNumber = model.DossierNumber,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser.UserId
            };
            db.Clients.Add(client);
        }

        var total = model.Lines.Sum(l => l.Quantity!.Value * l.UnitPrice!.Value);
        var downPayment = model.DownPayment ?? 0m;
        var financed = total - downPayment;

        // Montant de la mensualité : celui saisi si valide, sinon recalculé.
        var instalmentAmount = model.InstalmentAmount is { } am and > 0
            ? am
            : Math.Round(financed / model.NumberOfInstalments!.Value, 2);

        var plan = new InstalmentPlan
        {
            Client = client,
            SaleDate = model.SaleDate!.Value.Date,
            TotalAmount = total,
            DownPayment = downPayment,
            NumberOfInstalments = model.NumberOfInstalments!.Value,
            InstalmentAmount = instalmentAmount,
            Status = InstalmentPlanStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        db.InstalmentPlans.Add(plan);

        var products = await db.Products
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in model.Lines)
        {
            var product = products[line.ProductId!.Value];
            var quantity = line.Quantity!.Value;

            plan.Lines.Add(new SaleLine
            {
                Product = product,
                Quantity = quantity,
                UnitPrice = line.UnitPrice!.Value
            });

            // Sortie de stock : avant/après calculés ici, jamais saisis.
            var before = product.CurrentStockQuantity;
            var after = before - quantity;
            product.CurrentStockQuantity = after;

            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                MovementDate = plan.SaleDate,
                QuantityBefore = before,
                Type = StockMovementType.Out,
                OperatorName = client.FullName,
                QuantityAfter = after,
                Observation = $"Vente — plan de {plan.NumberOfInstalments} mensualité(s)",
                InstalmentPlan = plan,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser.UserId
            });
        }

        plan.Instalments = InstalmentSchedule.Build(plan, currentUser.UserId);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"Vente enregistrée — plan de {plan.NumberOfInstalments} mensualité(s), stock mis à jour.";
        return RedirectToAction(nameof(Details), new { id = plan.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var plan = await db.InstalmentPlans.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Product)
            .Include(p => p.Instalments)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        // Les quantités avant/après affichées viennent du grand livre, pas d'un
        // recalcul — l'écran reflète exactement ce qui a été écrit.
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.InstalmentPlanId == id)
            .ToDictionaryAsync(m => m.ProductId);

        var model = new SaleDetailsViewModel
        {
            Id = plan.Id,
            ClientId = plan.ClientId,
            ClientFullName = plan.Client.FullName,
            ClientAccountNumber = plan.Client.AccountNumber,
            ClientAccountKey = plan.Client.AccountKey,
            ClientPhone = plan.Client.Phone,
            SaleDate = plan.SaleDate,
            TotalAmount = plan.TotalAmount,
            DownPayment = plan.DownPayment,
            NumberOfInstalments = plan.NumberOfInstalments,
            InstalmentAmount = plan.InstalmentAmount,
            Status = plan.Status,
            CreatedAtUtc = plan.CreatedAtUtc,
            CreatedByName = plan.CreatedByUserId is { } cid
                ? await db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.Name).FirstOrDefaultAsync()
                : null,
            Lines = plan.Lines.Select(l =>
            {
                var hasMove = movements.TryGetValue(l.ProductId, out var move);
                return new SaleLineDetailViewModel
                {
                    ProductName = l.Product.Name,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    QuantityBefore = hasMove ? move!.QuantityBefore : 0,
                    QuantityAfter = hasMove ? move!.QuantityAfter : 0
                };
            }).ToList(),
            Instalments = plan.Instalments
                .OrderBy(i => i.DueDate)
                .Select(i => new InstalmentDetailViewModel
                {
                    Id = i.Id,
                    DueDate = i.DueDate,
                    Amount = i.Amount,
                    Status = i.Status
                }).ToList()
        };

        return View(model);
    }

    // Solde anticipé (#11 — décision business confirmée : aucune marchandise
    // n'est retournée). Quand un client veut clore son contrat, il règle en une
    // fois la totalité des mensualités restantes (En attente ou Échouées) : elles
    // passent toutes à Payée et le plan passe à Soldé. Le stock n'est pas touché
    // (la marchandise reste chez le client) et l'historique des lots déjà
    // générés reste intact — les mensualités payées ne sont simplement plus
    // reprises par les générations suivantes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SettleEarly(int id)
    {
        var plan = await db.InstalmentPlans
            .Include(p => p.Instalments)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null)
            return NotFound();

        var remaining = plan.Instalments.Where(i => i.Status != InstalmentStatus.Paid).ToList();
        if (remaining.Count == 0)
        {
            TempData["Error"] = "Toutes les mensualités sont déjà réglées — ce plan est déjà soldé.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var remainingAmount = remaining.Sum(i => i.Amount);
        var now = DateTime.UtcNow;

        foreach (var instalment in remaining)
        {
            instalment.Status = InstalmentStatus.Paid;
            instalment.UpdatedAtUtc = now;
            instalment.UpdatedByUserId = currentUser.UserId;
        }

        plan.Status = InstalmentPlanStatus.Completed;
        plan.UpdatedAtUtc = now;
        plan.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync();

        TempData["Success"] =
            $"Solde anticipé — {remaining.Count} mensualité(s) restante(s) réglée(s) en une fois ({remainingAmount.ToDa()}). " +
            "Le plan est clôturé.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ValidateAsync(CreateSaleViewModel model)
    {
        // --- Client ---
        if (model.ClientId is { } clientId)
        {
            var exists = await db.Clients.AsNoTracking().AnyAsync(c => c.Id == clientId);
            if (!exists)
                ModelState.AddModelError(string.Empty,
                    "Le client sélectionné n'existe plus — choisissez-en un autre.");
        }
        else
        {
            var hasAccount = !string.IsNullOrEmpty(model.AccountNumber);
            var hasKey = !string.IsNullOrEmpty(model.AccountKey);

            if (string.IsNullOrWhiteSpace(model.LastName) || string.IsNullOrWhiteSpace(model.FirstName))
                ModelState.AddModelError(string.Empty, "Renseignez le nom et le prénom du nouveau client.");
            if (!hasAccount || !hasKey)
                ModelState.AddModelError(string.Empty, "Le numéro de compte et la clé du client sont requis.");
            else
            {
                if (!AccountNumberRegex.IsMatch(model.AccountNumber!))
                    ModelState.AddModelError(string.Empty,
                        "Le numéro de compte du client doit contenir entre 5 et 20 chiffres, sans espaces ni lettres.");
                if (!AccountKeyRegex.IsMatch(model.AccountKey!))
                    ModelState.AddModelError(string.Empty, "La clé du compte doit contenir 1 ou 2 chiffres.");

                // Prévention des doublons (PRD §7.4) : même numéro de compte
                // → impossible d'en créer un second ; il faut le sélectionner.
                var duplicate = await db.Clients.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.AccountNumber == model.AccountNumber);
                if (duplicate is not null)
                    ModelState.AddModelError(string.Empty,
                        $"Un client existe déjà avec le compte {duplicate.AccountNumber} — {duplicate.FullName}. " +
                        "Sélectionnez-le via la recherche.");
            }
            if (model.Phone is not null && !PhoneRegex.IsMatch(model.Phone))
                ModelState.AddModelError(string.Empty,
                    "Le téléphone doit contenir 10 chiffres au format 0X XX XX XX XX.");
        }

        // --- Lignes de produits ---
        if (model.Lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Ajoutez au moins une ligne de produit à la vente.");

        var activeProducts = await db.Products
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in model.Lines)
        {
            if (line.ProductId is not { } pid)
            {
                ModelState.AddModelError(string.Empty, "Choisissez un produit pour chaque ligne.");
                continue;
            }

            if (!activeProducts.TryGetValue(pid, out var product))
            {
                ModelState.AddModelError(string.Empty,
                    "Un des produits choisis n'existe plus ou a été retiré du catalogue.");
                continue;
            }

            if (line.Quantity is { } qty && qty > product.CurrentStockQuantity)
                ModelState.AddModelError(string.Empty,
                    $"Stock insuffisant pour « {product.Name} » — disponible : {product.CurrentStockQuantity}.");
        }

        // Un même produit ne peut apparaître qu'une fois par vente — sinon les
        // quantités avant/après du mouvement seraient trompeuses.
        var duplicates = model.Lines.Where(l => l.ProductId.HasValue)
            .GroupBy(l => l.ProductId!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var dupId in duplicates)
        {
            if (activeProducts.TryGetValue(dupId, out var dup))
                ModelState.AddModelError(string.Empty,
                    $"Le produit « {dup.Name} » apparaît sur plusieurs lignes — regroupez-le en une seule ligne.");
        }

        // --- Conditions de paiement ---
        var total = model.Lines
            .Where(l => l.Quantity.HasValue && l.UnitPrice.HasValue)
            .Sum(l => l.Quantity!.Value * l.UnitPrice!.Value);

        foreach (var line in model.Lines)
        {
            if (line.ProductId.HasValue && (!line.Quantity.HasValue || !line.UnitPrice.HasValue))
                ModelState.AddModelError(string.Empty,
                    "Complétez la quantité et le prix unitaire de chaque ligne.");
        }

        var downPayment = model.DownPayment ?? 0m;
        if (downPayment < 0)
            ModelState.AddModelError(string.Empty, "L'acompte doit être positif ou nul.");
        if (downPayment > total)
            ModelState.AddModelError(string.Empty, "L'acompte ne peut pas dépasser le montant total de la vente.");

        var financed = total - downPayment;
        if (model.NumberOfInstalments is not { } n)
            ModelState.AddModelError(string.Empty, "Indiquez le nombre de mensualités.");
        else if (model.InstalmentAmount is { } amt && amt <= 0)
            ModelState.AddModelError(string.Empty, "Le montant de la mensualité doit être strictement positif.");
        else if (financed <= 0)
            ModelState.AddModelError(string.Empty,
                "Le montant à financer doit être positif — baissez l'acompte.");
        else if (model.InstalmentAmount is { } amt2 && amt2 * (n - 1) >= financed)
            ModelState.AddModelError(string.Empty,
                "Le montant de la mensualité est trop élevé — la dernière mensualité serait négative.");
    }

    private async Task PopulateOptionsAsync(CreateSaleViewModel model)
    {
        // Seuls les produits actifs sont proposés à la vente (critère #4.2).
        model.Products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new SaleProductOption(p.Id, p.Name, p.UnitSalePrice, p.CurrentStockQuantity))
            .ToListAsync();
    }
}
