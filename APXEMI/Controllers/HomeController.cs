using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace APXEMI.Controllers;

// Tableau de bord (PRD §7.9) — page d'accueil après connexion. Le Propriétaire
// voit le magasin entier (mensualités dues, activité de tous les employés,
// stock bas, lots à rapprocher) ; le Vendeur ne voit que ses propres ventes
// récentes et le stock courant. Rien n'est mis en cache : chaque chargement
// relit la base, donc les compteurs reflètent toujours les enregistrements.
[Authorize]
public class HomeController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    // Seuil de « stock bas » affiché sur le tableau de bord (carte Propriétaire).
    private const int LowStockThreshold = 5;

    public async Task<IActionResult> Index()
    {
        var model = new DashboardViewModel
        {
            IsOwner = currentUser.IsOwner,
            UserName = currentUser.Name ?? string.Empty
        };

        if (currentUser.IsOwner)
            model.Owner = await BuildOwnerDashboardAsync();
        else if (currentUser.UserId is { } sellerId)
            model.Seller = await BuildSellerDashboardAsync(sellerId);

        return View(model);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<OwnerDashboardViewModel> BuildOwnerDashboardAsync()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var dueThisMonth = await db.Instalments.AsNoTracking()
            .Where(i => i.Status == InstalmentStatus.Pending
                && i.DueDate >= monthStart && i.DueDate < monthEnd)
            .Select(i => i.Amount)
            .ToListAsync();

        var overdue = await db.Instalments.AsNoTracking()
            .Where(i => i.Status == InstalmentStatus.Pending && i.DueDate < monthStart)
            .Select(i => i.Amount)
            .ToListAsync();

        var salesThisMonth = await db.InstalmentPlans.AsNoTracking()
            .Where(p => p.SaleDate >= monthStart && p.SaleDate < monthEnd)
            .Select(p => p.TotalAmount)
            .ToListAsync();

        var collectedThisMonth = await db.Instalments.AsNoTracking()
            .Where(i => i.Status == InstalmentStatus.Paid
                && i.UpdatedAtUtc >= monthStart && i.UpdatedAtUtc < monthEnd)
            .Select(i => i.Amount)
            .ToListAsync();

        // Lots de prélèvement non entièrement rapprochés (#10) — ce qui reste
        // à comptabiliser pour clôturer le mois.
        var batches = await db.PrelevementBatches.AsNoTracking()
            .Include(b => b.Lines)
            .Where(b => b.ReconciledAtUtc == null)
            .OrderByDescending(b => b.ReferenceMonth.Substring(3))
            .ThenByDescending(b => b.ReferenceMonth)
            .ToListAsync();

        // Dernière activité du magasin : les mouvements de stock, tous employés.
        var recentMoves = await db.StockMovements.AsNoTracking()
            .Include(m => m.Product)
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.Id)
            .Take(10)
            .ToListAsync();

        var staffIds = recentMoves.Where(m => m.CreatedByUserId.HasValue)
            .Select(m => m.CreatedByUserId!.Value).Distinct().ToList();
        var staffNames = await db.Users.AsNoTracking()
            .Where(u => staffIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var lowStock = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CurrentStockQuantity <= LowStockThreshold)
            .OrderBy(p => p.CurrentStockQuantity)
            .ThenBy(p => p.Name)
            .Take(10)
            .Select(p => new DashboardStockItem
            {
                Id = p.Id,
                Name = p.Name,
                CurrentStockQuantity = p.CurrentStockQuantity
            })
            .ToListAsync();

        return new OwnerDashboardViewModel
        {
            DueThisMonthCount = dueThisMonth.Count,
            DueThisMonthAmount = dueThisMonth.Sum(),
            OverdueCount = overdue.Count,
            OverdueAmount = overdue.Sum(),
            SalesThisMonthCount = salesThisMonth.Count,
            SalesThisMonthAmount = salesThisMonth.Sum(),
            CollectedThisMonthCount = collectedThisMonth.Count,
            CollectedThisMonthAmount = collectedThisMonth.Sum(),
            BatchesPending = batches.Select(b => new DashboardBatchItem
            {
                Id = b.Id,
                ReferenceMonth = b.ReferenceMonth,
                TotalCount = b.Lines.Count,
                UnreconciledCount = b.Lines.Count(l => l.ReconcileStatus == InstalmentStatus.Pending)
            }).ToList(),
            RecentActivity = recentMoves.Select(m => new DashboardMovementItem
            {
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                MovementDate = m.MovementDate,
                Type = m.Type,
                OperatorName = m.OperatorName,
                Quantity = Math.Abs(m.QuantityAfter - m.QuantityBefore),
                StaffName = m.CreatedByUserId is { } uid && staffNames.TryGetValue(uid, out var n) ? n : null
            }).ToList(),
            LowStock = lowStock
        };
    }

    private async Task<SellerDashboardViewModel> BuildSellerDashboardAsync(int sellerId)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var salesThisMonth = await db.InstalmentPlans.AsNoTracking()
            .Where(p => p.CreatedByUserId == sellerId
                && p.SaleDate >= monthStart && p.SaleDate < monthEnd)
            .Select(p => p.TotalAmount)
            .ToListAsync();

        // Mes ventes les plus récentes — uniquement celles du vendeur courant.
        var recentSales = await db.InstalmentPlans.AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.CreatedByUserId == sellerId)
            .OrderByDescending(p => p.SaleDate)
            .ThenByDescending(p => p.Id)
            .Take(8)
            .ToListAsync();

        // Stock courant : tous les produits actifs, le plus bas d'abord.
        var stock = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.CurrentStockQuantity)
            .ThenBy(p => p.Name)
            .Select(p => new DashboardStockItem
            {
                Id = p.Id,
                Name = p.Name,
                CurrentStockQuantity = p.CurrentStockQuantity
            })
            .ToListAsync();

        return new SellerDashboardViewModel
        {
            SalesThisMonthCount = salesThisMonth.Count,
            SalesThisMonthAmount = salesThisMonth.Sum(),
            LowStockCount = stock.Count(s => s.CurrentStockQuantity <= LowStockThreshold),
            RecentSales = recentSales.Select(p => new DashboardSaleItem
            {
                Id = p.Id,
                SaleDate = p.SaleDate,
                ClientFullName = p.Client.FullName,
                TotalAmount = p.TotalAmount,
                Status = p.Status
            }).ToList(),
            CurrentStock = stock
        };
    }
}
