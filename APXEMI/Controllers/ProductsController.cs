using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Catalogue produits (PRD §5, §7.3) : tout le personnel peut consulter et ajouter ;
// seul le Propriétaire peut modifier ou retirer un produit — appliqué côté serveur.
// Le stock affiché est calculé par le système (issue #5/#7), jamais saisi à la main.
// La catégorie et la marque sont choisies dans les listes gérées (menu déroulant),
// plus jamais de texte libre.
[Authorize]
public class ProductsController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var model = products.Select(p => new ProductListItemViewModel
        {
            Id = p.Id,
            Name = p.Name,
            CategoryName = p.Category?.Name ?? string.Empty,
            BrandName = p.Brand?.Name ?? string.Empty,
            UnitCost = p.UnitCost,
            UnitSalePrice = p.UnitSalePrice,
            CurrentStockQuantity = p.CurrentStockQuantity,
            IsActive = p.IsActive
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateProductViewModel();
        model.CategoryOptions = await CategoryOptionsAsync();
        model.BrandOptions = await BrandOptionsAsync();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductViewModel model)
    {
        model.Name = model.Name.Trim();

        if (ModelState.IsValid && !await CategoryExistsActiveAsync(model.CategoryId))
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Cette catégorie n'est plus disponible.");
        }
        if (ModelState.IsValid && !await BrandExistsActiveAsync(model.BrandId))
        {
            ModelState.AddModelError(nameof(model.BrandId), "Cette marque n'est plus disponible.");
        }

        if (!ModelState.IsValid)
        {
            model.CategoryOptions = await CategoryOptionsAsync(model.CategoryId);
            model.BrandOptions = await BrandOptionsAsync(model.BrandId);
            return View(model);
        }

        var product = new Product
        {
            Name = model.Name,
            CategoryId = model.CategoryId,
            BrandId = model.BrandId,
            UnitCost = model.UnitCost,
            UnitSalePrice = model.UnitSalePrice,
            CurrentStockQuantity = 0, // le stock démarre à zéro, alimenté par les achats (#5)
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Le produit « {product.Name} » a été ajouté au catalogue.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Owner))]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            return NotFound();

        var model = new EditProductViewModel
        {
            Id = product.Id,
            Name = product.Name,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            UnitCost = product.UnitCost,
            UnitSalePrice = product.UnitSalePrice,
            CurrentStockQuantity = product.CurrentStockQuantity,
            IsActive = product.IsActive,
            CreatedAtUtc = product.CreatedAtUtc,
            UpdatedAtUtc = product.UpdatedAtUtc
        };

        var auditUserIds = new[] { product.CreatedByUserId, product.UpdatedByUserId }
            .Where(uid => uid.HasValue).Select(uid => uid!.Value).Distinct().ToList();
        var namesById = await db.Users.AsNoTracking()
            .Where(u => auditUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);
        model.CreatedByName = product.CreatedByUserId is { } cid && namesById.TryGetValue(cid, out var cn) ? cn : null;
        model.UpdatedByName = product.UpdatedByUserId is { } uid && namesById.TryGetValue(uid, out var un) ? un : null;

        // La valeur actuelle du produit reste proposée même si la catégorie/
        // marque a été désactivée depuis, pour ne jamais la perdre au ré-enregistrement.
        model.CategoryOptions = await CategoryOptionsAsync(product.CategoryId);
        model.BrandOptions = await BrandOptionsAsync(product.BrandId);
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditProductViewModel model)
    {
        if (id != model.Id)
            return BadRequest();

        var product = await db.Products.FindAsync(id);
        if (product is null)
            return NotFound();

        model.Name = model.Name.Trim();

        if (ModelState.IsValid && !await db.Categories.AnyAsync(c => c.Id == model.CategoryId))
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Cette catégorie n'existe plus.");
        }
        if (ModelState.IsValid && !await db.Brands.AnyAsync(b => b.Id == model.BrandId))
        {
            ModelState.AddModelError(nameof(model.BrandId), "Cette marque n'existe plus.");
        }

        if (!ModelState.IsValid)
        {
            // Réaffiche les infos système inchangées avec le formulaire.
            model.CurrentStockQuantity = product.CurrentStockQuantity;
            model.IsActive = product.IsActive;
            model.CreatedAtUtc = product.CreatedAtUtc;
            model.UpdatedAtUtc = product.UpdatedAtUtc;
            model.CategoryOptions = await CategoryOptionsAsync(model.CategoryId);
            model.BrandOptions = await BrandOptionsAsync(model.BrandId);
            return View(model);
        }

        product.Name = model.Name;
        product.CategoryId = model.CategoryId;
        product.BrandId = model.BrandId;
        product.UnitCost = model.UnitCost;
        product.UnitSalePrice = model.UnitSalePrice;
        // CurrentStockQuantity et IsActive ne sont jamais modifiés ici.
        product.UpdatedAtUtc = DateTime.UtcNow;
        product.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = "Modifications enregistrées avec succès.";
        return RedirectToAction(nameof(Index));
    }

    // Retirer / réactiver : un produit retiré n'est plus proposé lors des nouveaux
    // achats ou ventes (#5/#7 filtreront sur IsActive) mais reste sur l'historique.
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
            return NotFound();

        product.IsActive = !product.IsActive;
        product.UpdatedAtUtc = DateTime.UtcNow;
        product.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = product.IsActive
            ? $"Le produit « {product.Name} » a été réactivé."
            : $"Le produit « {product.Name} » a été retiré. Il reste visible sur l'historique.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> CategoryOptionsAsync(int? includeInactiveId = null)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive || c.Id == includeInactiveId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }

    private async Task<List<SelectListItem>> BrandOptionsAsync(int? includeInactiveId = null)
    {
        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.IsActive || b.Id == includeInactiveId)
            .OrderBy(b => b.Name)
            .ToListAsync();
        return brands.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
    }

    private async Task<bool> CategoryExistsActiveAsync(int id) =>
        await db.Categories.AnyAsync(c => c.Id == id && c.IsActive);

    private async Task<bool> BrandExistsActiveAsync(int id) =>
        await db.Brands.AnyAsync(b => b.Id == id && b.IsActive);
}
