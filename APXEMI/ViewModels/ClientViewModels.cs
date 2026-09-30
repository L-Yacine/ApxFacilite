using System.ComponentModel.DataAnnotations;
using APXEMI.Models;

namespace APXEMI.ViewModels;

// --- Recherche de clients (PRD §7.4) ---

public class ClientIndexViewModel
{
    [Display(Name = "Rechercher un client")]
    public string? Q { get; set; }

    public List<ClientListItemViewModel> Clients { get; set; } = new();
}

public class ClientListItemViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int PlanCount { get; set; }
    public int ActivePlanCount { get; set; }

    public string? PhoneDisplay =>
        Phone is { Length: 10 } p ? $"{p[..2]} {p[2..4]} {p[4..6]} {p[6..8]} {p[8..]}" : Phone;
}

// --- Profil d'un client (PRD §7.4, #8) ---
// Liste TOUS les plans du client, passés et en cours, chacun avec ses
// produits, ses conditions et son solde payé/restant — jamais fusionnés (§6).

public class ClientDetailsViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? DossierNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }

    public List<ClientPlanListItemViewModel> Plans { get; set; } = new();

    public int ActivePlanCount => Plans.Count(p => p.Status == InstalmentPlanStatus.Active);

    public string? PhoneDisplay =>
        Phone is { Length: 10 } p ? $"{p[..2]} {p[2..4]} {p[4..6]} {p[6..8]} {p[8..]}" : Phone;
}

public class ClientPlanListItemViewModel
{
    public int PlanId { get; set; }
    public DateTime SaleDate { get; set; }
    public InstalmentPlanStatus Status { get; set; }
    public string Products { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal DownPayment { get; set; }
    public int NumberOfInstalments { get; set; }
    public decimal InstalmentAmount { get; set; }

    // Solde calculé par le système (PRD §7.5) : acompte + mensualités payées,
    // jamais saisi.
    public decimal AmountPaid { get; set; }
    public decimal AmountRemaining => TotalAmount - AmountPaid;
}
