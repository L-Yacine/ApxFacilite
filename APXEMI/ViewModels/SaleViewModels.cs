using System.ComponentModel.DataAnnotations;
using APXEMI.Models;

namespace APXEMI.ViewModels;

// Produit proposé à la vente : seuls les produits actifs du catalogue (§4.2),
// avec le prix de vente à pré-remplir et le stock disponible à contrôler.
public record SaleProductOption(int Id, string Name, decimal UnitSalePrice, int Stock);

public record ClientSearchResult(int Id, string FullName, string AccountNumber, string? Phone);

// --- Historique des ventes ---

public class SaleIndexViewModel
{
    [Display(Name = "Client")]
    public string? Search { get; set; }

    public List<SaleListItemViewModel> Sales { get; set; } = new();
}

public class SaleListItemViewModel
{
    public int Id { get; set; }
    public DateTime SaleDate { get; set; }
    public string ClientFullName { get; set; } = string.Empty;
    public string ClientAccountNumber { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DownPayment { get; set; }
    public InstalmentPlanStatus Status { get; set; }
    public string? CreatedByName { get; set; }
}

// --- Saisie d'une vente ---

public class SaleLineInputViewModel
{
    [Required(ErrorMessage = "Choisissez un produit pour chaque ligne.")]
    [Display(Name = "Produit")]
    public int? ProductId { get; set; }

    [Required(ErrorMessage = "La quantité est requise.")]
    [Range(1, 100000, ErrorMessage = "La quantité doit être d'au moins 1.")]
    [Display(Name = "Quantité")]
    public int? Quantity { get; set; }

    [Required(ErrorMessage = "Le prix unitaire est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le prix unitaire doit être positif ou nul.")]
    [Display(Name = "Prix unitaire (DA)")]
    public decimal? UnitPrice { get; set; }
}

public class CreateSaleViewModel
{
    // --- Client : sélection d'un existant (recherche, PRD §7.4) ---

    public int? ClientId { get; set; }

    // Champ caché — reflète le client choisi via la recherche (affichage).
    public string? ClientFullName { get; set; }

    [Display(Name = "Rechercher un client")]
    public string? ClientSearch { get; set; }

    // --- Ou création d'un nouveau client (champs validés côté serveur) ---

    [Display(Name = "N° de compte")]
    public string? AccountNumber { get; set; }

    [Display(Name = "Clé")]
    public string? AccountKey { get; set; }

    [Display(Name = "Nom")]
    public string? LastName { get; set; }

    [Display(Name = "Prénom")]
    public string? FirstName { get; set; }

    [Display(Name = "Téléphone")]
    public string? Phone { get; set; }

    [MaxLength(50, ErrorMessage = "Le n° de dossier ne peut pas dépasser 50 caractères.")]
    [Display(Name = "N° de dossier (facultatif)")]
    public string? DossierNumber { get; set; }

    // --- Produits vendus ---

    public List<SaleLineInputViewModel> Lines { get; set; } = new() { new SaleLineInputViewModel() };

    public List<SaleProductOption> Products { get; set; } = new();

    // --- Conditions de paiement ---

    [Required(ErrorMessage = "La date de vente est requise.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date de vente")]
    public DateTime? SaleDate { get; set; }

    [Range(0, 999999999.99, ErrorMessage = "L'acompte doit être positif ou nul.")]
    [Display(Name = "Acompte (DA)")]
    public decimal? DownPayment { get; set; }

    [Required(ErrorMessage = "Le nombre de mensualités est requis.")]
    [Range(1, 24, ErrorMessage = "Le nombre de mensualités doit être entre 1 et 24.")]
    [Display(Name = "Nombre de mensualités")]
    public int? NumberOfInstalments { get; set; }

    // Montant d'une mensualité — pré-calculé par le navigateur, modifiable
    // avant confirmation (PRD §7.5). Si vide, le serveur le recalcule.
    [Display(Name = "Mensualité (DA)")]
    public decimal? InstalmentAmount { get; set; }
}

// --- Détail d'une vente ---

public class SaleDetailsViewModel
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientFullName { get; set; } = string.Empty;
    public string ClientAccountNumber { get; set; } = string.Empty;
    public string ClientAccountKey { get; set; } = string.Empty;
    public string? ClientPhone { get; set; }
    public DateTime SaleDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DownPayment { get; set; }
    public decimal FinancedAmount => TotalAmount - DownPayment;
    public int NumberOfInstalments { get; set; }
    public decimal InstalmentAmount { get; set; }
    public InstalmentPlanStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public List<SaleLineDetailViewModel> Lines { get; set; } = new();
    public List<InstalmentDetailViewModel> Instalments { get; set; } = new();

    // Soldes toujours calculés par le système (PRD §7.5) — jamais saisis.
    public decimal AmountPaid => DownPayment + Instalments
        .Where(i => i.Status == InstalmentStatus.Paid).Sum(i => i.Amount);

    public decimal AmountRemaining => TotalAmount - AmountPaid;

    // Solde anticipé (#11) : mensualités encore à régler (En attente ou
    // Échouées) et leur somme — ce que le client doit pour clôturer le plan.
    public int RemainingInstalmentCount =>
        Instalments.Count(i => i.Status != InstalmentStatus.Paid);

    public decimal RemainingSettlementAmount => Instalments
        .Where(i => i.Status != InstalmentStatus.Paid).Sum(i => i.Amount);

    public bool CanSettleEarly => RemainingInstalmentCount > 0;

    public string? ClientPhoneDisplay =>
        ClientPhone is { Length: 10 } p ? $"{p[..2]} {p[2..4]} {p[4..6]} {p[6..8]} {p[8..]}" : ClientPhone;
}

public class SaleLineDetailViewModel
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;

    // Quantités avant/après issues du mouvement de stock généré (jamais saisies).
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
}

public class InstalmentDetailViewModel
{
    public int Id { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public InstalmentStatus Status { get; set; }
    public string ReferenceMonth => DueDate.ToString("MM/yyyy");
}
