using APXEMI.Models;

namespace APXEMI.ViewModels;

// --- Fiche de stock (PRD §7.3, §9) : le grand livre d'un produit ---

public class StockLedgerIndexViewModel
{
    public List<ProductOption> Products { get; set; } = new();

    public int? SelectedProductId { get; set; }

    // Renseignés uniquement quand un produit est sélectionné.
    public string? ProductName { get; set; }

    public int? CurrentStockQuantity { get; set; }

    public List<StockMovementRowViewModel> Movements { get; set; } = new();
}

public class StockMovementRowViewModel
{
    public DateTime MovementDate { get; set; }

    public int QuantityBefore { get; set; }

    public StockMovementType Type { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public int QuantityAfter { get; set; }

    public string? Observation { get; set; }
}
