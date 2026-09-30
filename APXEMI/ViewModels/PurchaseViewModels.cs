using System.ComponentModel.DataAnnotations;
using APXEMI.Models;

namespace APXEMI.ViewModels;

public record SupplierOption(int Id, string Name);

public record ProductOption(int Id, string Name, decimal UnitCost);

// --- Historique des achats (PRD §7.2 : par fournisseur, produit ou période) ---

public class PurchaseIndexViewModel
{
    public int? SupplierId { get; set; }

    public int? ProductId { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Du")]
    public DateTime? DateFrom { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Au")]
    public DateTime? DateTo { get; set; }

    public List<SupplierOption> Suppliers { get; set; } = new();

    public List<ProductOption> Products { get; set; } = new();

    public List<PurchaseListItemViewModel> Purchases { get; set; } = new();
}

public class PurchaseListItemViewModel
{
    public int Id { get; set; }
    public DateTime InvoiceDate { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public PurchasePaymentMethod PaymentMethod { get; set; }
    public string? CheckNumber { get; set; }
    public int LineCount { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CreatedByName { get; set; }
}

// --- Saisie d'un achat ---

public class PurchaseLineInputViewModel
{
    [Required(ErrorMessage = "Choisissez un produit pour chaque ligne.")]
    [Display(Name = "Produit")]
    public int? ProductId { get; set; }

    [Required(ErrorMessage = "La quantité est requise.")]
    [Range(1, 100000, ErrorMessage = "La quantité doit être d'au moins 1.")]
    [Display(Name = "Quantité")]
    public int? Quantity { get; set; }

    [Required(ErrorMessage = "Le coût unitaire est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le coût unitaire doit être positif ou nul.")]
    [Display(Name = "Coût unitaire (DA)")]
    public decimal? UnitCost { get; set; }
}

public class CreatePurchaseViewModel
{
    [Required(ErrorMessage = "Le fournisseur est requis.")]
    [MaxLength(150, ErrorMessage = "Le nom du fournisseur ne peut pas dépasser 150 caractères.")]
    [Display(Name = "Fournisseur")]
    public string SupplierName { get; set; } = string.Empty;

    // Utilisé uniquement si le fournisseur saisi n'existe pas encore.
    [MaxLength(20, ErrorMessage = "Le téléphone ne peut pas dépasser 20 caractères.")]
    [Display(Name = "Téléphone (nouveau fournisseur)")]
    public string? SupplierPhone { get; set; }

    [Required(ErrorMessage = "Le numéro de facture est requis.")]
    [MaxLength(50, ErrorMessage = "Le numéro de facture ne peut pas dépasser 50 caractères.")]
    [Display(Name = "N° de facture")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "La date de facture est requise.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date de facture")]
    public DateTime? InvoiceDate { get; set; }

    [Required(ErrorMessage = "Le mode de règlement est requis.")]
    [Display(Name = "Mode de règlement")]
    public PurchasePaymentMethod? PaymentMethod { get; set; }

    [MaxLength(50, ErrorMessage = "Le numéro de chèque ne peut pas dépasser 50 caractères.")]
    [Display(Name = "N° de chèque (optionnel)")]
    public string? CheckNumber { get; set; }

    public List<PurchaseLineInputViewModel> Lines { get; set; } = new() { new PurchaseLineInputViewModel() };

    // Données des listes déroulantes.
    public List<SupplierOption> Suppliers { get; set; } = new();

    public List<ProductOption> Products { get; set; } = new();
}

// --- Détail d'un achat ---

public class PurchaseDetailsViewModel
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public PurchasePaymentMethod PaymentMethod { get; set; }
    public string? CheckNumber { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public List<PurchaseLineDetailViewModel> Lines { get; set; } = new();
}

public class PurchaseLineDetailViewModel
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal => Quantity * UnitCost;

    // Quantités avant/après issues du mouvement de stock généré (jamais saisies).
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
}
