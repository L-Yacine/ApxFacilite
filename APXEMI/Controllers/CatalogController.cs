using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Catégories & marques (liste gérée, PRD §7.3) : tout le personnel peut en
// créer ; seul le Propriétaire peut renommer ou désactiver (même règle que
// les produits). Une entrée désactivée disparaît des menus déroulants des
// formulaires produit mais reste visible sur l'historique — jamais supprimée.
[Authorize]
public class CatalogController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return View(await BuildIndexAsync());
    }

    [HttpGet]
    public IActionResult CreateCategory() => View(new CreateCategoryViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CreateCategoryViewModel model)
    {
        model.Name = model.Name.Trim();

        if (await db.Categories.AnyAsync(c => c.Name == model.Name))
            ModelState.AddModelError(nameof(model.Name), "Cette catégorie existe déjà.");

        if (!ModelState.IsValid)
            return View(model);

        db.Categories.Add(new Category
        {
            Name = model.Name,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        });
        await db.SaveChangesAsync();

        TempData["Success"] = $"La catégorie « {model.Name} » a été ajoutée.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult CreateBrand() => View(new CreateBrandViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBrand(CreateBrandViewModel model)
    {
        model.Name = model.Name.Trim();

        if (await db.Brands.AnyAsync(b => b.Name == model.Name))
            ModelState.AddModelError(nameof(model.Name), "Cette marque existe déjà.");

        if (!ModelState.IsValid)
            return View(model);

        db.Brands.Add(new Brand
        {
            Name = model.Name,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        });
        await db.SaveChangesAsync();

        TempData["Success"] = $"La marque « {model.Name} » a été ajoutée.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Owner))]
    public async Task<IActionResult> EditCategory(int id)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
            return NotFound();

        var model = new EditCategoryViewModel
        {
            Id = category.Id,
            Name = category.Name,
            IsActive = category.IsActive,
            CreatedAtUtc = category.CreatedAtUtc
        };

        var namesById = await LoadAuditNamesAsync(
            new[] { category.CreatedByUserId, category.UpdatedByUserId }
                .Where(uid => uid.HasValue).Select(uid => uid!.Value));
        model.CreatedByName = category.CreatedByUserId is { } cid && namesById.TryGetValue(cid, out var cn) ? cn : null;
        model.UpdatedByName = category.UpdatedByUserId is { } uid && namesById.TryGetValue(uid, out var un) ? un : null;
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, EditCategoryViewModel model)
    {
        if (id != model.Id)
            return BadRequest();

        var category = await db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        model.Name = model.Name.Trim();
        model.IsActive = category.IsActive;

        if (await db.Categories.AnyAsync(c => c.Name == model.Name && c.Id != id))
            ModelState.AddModelError(nameof(model.Name), "Cette catégorie existe déjà.");

        if (!ModelState.IsValid)
        {
            model.CreatedAtUtc = category.CreatedAtUtc;
            return View(model);
        }

        category.Name = model.Name;
        category.UpdatedAtUtc = DateTime.UtcNow;
        category.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = "Modifications enregistrées avec succès.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Owner))]
    public async Task<IActionResult> EditBrand(int id)
    {
        var brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (brand is null)
            return NotFound();

        var model = new EditBrandViewModel
        {
            Id = brand.Id,
            Name = brand.Name,
            IsActive = brand.IsActive,
            CreatedAtUtc = brand.CreatedAtUtc
        };

        var namesById = await LoadAuditNamesAsync(
            new[] { brand.CreatedByUserId, brand.UpdatedByUserId }
                .Where(uid => uid.HasValue).Select(uid => uid!.Value));
        model.CreatedByName = brand.CreatedByUserId is { } cid && namesById.TryGetValue(cid, out var cn) ? cn : null;
        model.UpdatedByName = brand.UpdatedByUserId is { } uid && namesById.TryGetValue(uid, out var un) ? un : null;
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBrand(int id, EditBrandViewModel model)
    {
        if (id != model.Id)
            return BadRequest();

        var brand = await db.Brands.FindAsync(id);
        if (brand is null)
            return NotFound();

        model.Name = model.Name.Trim();
        model.IsActive = brand.IsActive;

        if (await db.Brands.AnyAsync(b => b.Name == model.Name && b.Id != id))
            ModelState.AddModelError(nameof(model.Name), "Cette marque existe déjà.");

        if (!ModelState.IsValid)
        {
            model.CreatedAtUtc = brand.CreatedAtUtc;
            return View(model);
        }

        brand.Name = model.Name;
        brand.UpdatedAtUtc = DateTime.UtcNow;
        brand.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = "Modifications enregistrées avec succès.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCategory(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        category.IsActive = !category.IsActive;
        category.UpdatedAtUtc = DateTime.UtcNow;
        category.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = category.IsActive
            ? $"La catégorie « {category.Name} » a été réactivée."
            : $"La catégorie « {category.Name} » a été désactivée. Elle n'est plus proposée lors de la création de produits, mais reste visible sur l'historique.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBrand(int id)
    {
        var brand = await db.Brands.FindAsync(id);
        if (brand is null)
            return NotFound();

        brand.IsActive = !brand.IsActive;
        brand.UpdatedAtUtc = DateTime.UtcNow;
        brand.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = brand.IsActive
            ? $"La marque « {brand.Name} » a été réactivée."
            : $"La marque « {brand.Name} » a été désactivée. Elle n'est plus proposée lors de la création de produits, mais reste visible sur l'historique.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<CatalogIndexViewModel> BuildIndexAsync()
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync();

        var categoryCounts = await db.Products.AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var brandCounts = await db.Products.AsNoTracking()
            .GroupBy(p => p.BrandId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var auditUserIds = categories
            .SelectMany(c => new[] { c.CreatedByUserId, c.UpdatedByUserId })
            .Concat(brands.SelectMany(b => new[] { b.CreatedByUserId, b.UpdatedByUserId }))
            .Where(uid => uid.HasValue).Select(uid => uid!.Value).Distinct().ToList();
        var namesById = await LoadAuditNamesAsync(auditUserIds);

        return new CatalogIndexViewModel
        {
            Categories = categories.Select(c => new CategoryListItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive,
                ProductCount = categoryCounts.GetValueOrDefault(c.Id),
                CreatedAtUtc = c.CreatedAtUtc,
                CreatedByName = c.CreatedByUserId is { } cid && namesById.TryGetValue(cid, out var cn) ? cn : null,
                UpdatedAtUtc = c.UpdatedAtUtc,
                UpdatedByName = c.UpdatedByUserId is { } uid && namesById.TryGetValue(uid, out var un) ? un : null
            }).ToList(),
            Brands = brands.Select(b => new BrandListItemViewModel
            {
                Id = b.Id,
                Name = b.Name,
                IsActive = b.IsActive,
                ProductCount = brandCounts.GetValueOrDefault(b.Id),
                CreatedAtUtc = b.CreatedAtUtc,
                CreatedByName = b.CreatedByUserId is { } cid && namesById.TryGetValue(cid, out var cn) ? cn : null,
                UpdatedAtUtc = b.UpdatedAtUtc,
                UpdatedByName = b.UpdatedByUserId is { } uid && namesById.TryGetValue(uid, out var un) ? un : null
            }).ToList()
        };
    }

    private async Task<Dictionary<int, string>> LoadAuditNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);
    }
}
