using APXEMI.Models;

namespace APXEMI.ViewModels;

// Tableau de bord (PRD §7.9) — page d'accueil après connexion, vue différente
// par rôle. Propriétaire : chiffres du magasin entier. Vendeur : uniquement
// ses propres ventes récentes et le stock courant. Toutes les valeurs sont
// recalculées à chaque chargement — aucun compteur mis en cache.

public class DashboardViewModel
{
    public bool IsOwner { get; set; }

    public string UserName { get; set; } = string.Empty;

    public OwnerDashboardViewModel? Owner { get; set; }

    public SellerDashboardViewModel? Seller { get; set; }
}

public class OwnerDashboardViewModel
{
    // Mensualités en attente dont l'échéance tombe dans le mois courant.
    public int DueThisMonthCount { get; set; }

    public decimal DueThisMonthAmount { get; set; }

    // Mensualités en attente passées de date (échéance avant le début du mois).
    public int OverdueCount { get; set; }

    public decimal OverdueAmount { get; set; }

    // Ventes enregistrées ce mois, tout le magasin confondu.
    public int SalesThisMonthCount { get; set; }

    public decimal SalesThisMonthAmount { get; set; }

    // Encaissé ce mois : mensualités passées à Payée (rapprochement #10 ou
    // solde anticipé #11) dont la mise à jour tombe dans le mois courant.
    public int CollectedThisMonthCount { get; set; }

    public decimal CollectedThisMonthAmount { get; set; }

    // Lots de prélèvement générés et pas encore entièrement rapprochés (#10).
    public List<DashboardBatchItem> BatchesPending { get; set; } = new();

    // Dernière activité du magasin : mouvements de stock de tous les employés.
    public List<DashboardMovementItem> RecentActivity { get; set; } = new();

    // Produits actifs au stock bas (seuil ≤ 5).
    public List<DashboardStockItem> LowStock { get; set; } = new();
}

public class SellerDashboardViewModel
{
    // Ventes de ce mois enregistrées par le vendeur courant — jamais de
    // chiffres du magasin entier pour un vendeur (PRD §7.9).
    public int SalesThisMonthCount { get; set; }

    public decimal SalesThisMonthAmount { get; set; }

    public int LowStockCount { get; set; }

    // Mes ventes les plus récentes.
    public List<DashboardSaleItem> RecentSales { get; set; } = new();

    // Stock courant de tous les produits actifs, le plus bas d'abord.
    public List<DashboardStockItem> CurrentStock { get; set; } = new();
}

public class DashboardBatchItem
{
    public int Id { get; set; }

    public string ReferenceMonth { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public int UnreconciledCount { get; set; }
}

public class DashboardMovementItem
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public DateTime MovementDate { get; set; }

    public StockMovementType Type { get; set; }

    // Fournisseur (entrée) ou client (sortie) — nom figé au moment de l'écriture.
    public string OperatorName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    // Employé ayant enregistré l'opération (traçabilité §10).
    public string? StaffName { get; set; }
}

public class DashboardSaleItem
{
    public int Id { get; set; }

    public DateTime SaleDate { get; set; }

    public string ClientFullName { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public InstalmentPlanStatus Status { get; set; }
}

public class DashboardStockItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int CurrentStockQuantity { get; set; }
}
