using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

public enum InstalmentPlanStatus
{
    Active,
    Completed,
    Voided
}

public static class InstalmentPlanStatusExtensions
{
    public static string ToFrenchLabel(this InstalmentPlanStatus status) => status switch
    {
        InstalmentPlanStatus.Active => "En cours",
        InstalmentPlanStatus.Completed => "Soldé",
        InstalmentPlanStatus.Voided => "Annulé",
        _ => status.ToString()
    };
}

public enum InstalmentStatus
{
    Pending,
    Paid,
    Failed
}

public static class InstalmentStatusExtensions
{
    public static string ToFrenchLabel(this InstalmentStatus status) => status switch
    {
        InstalmentStatus.Pending => "En attente",
        InstalmentStatus.Paid => "Payée",
        InstalmentStatus.Failed => "Échouée",
        _ => status.ToString()
    };
}

// Plan de mensualités = une vente (PRD §7.5, §8). Chaque vente est SON propre
// plan, jamais fusionné avec les autres plans du client (§6). À l'enregistrement
// le système génère le calendrier des mensualités et écrit les sorties de stock.
public class InstalmentPlan
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public DateTime SaleDate { get; set; }

    // Prix total des produits vendus (somme des lignes), jamais saisi.
    public decimal TotalAmount { get; set; }

    // Acompte encaissé à la vente (décision confirmée) ; le reste
    // (TotalAmount − DownPayment) est réparti sur les mensualités.
    public decimal DownPayment { get; set; }

    public int NumberOfInstalments { get; set; }

    // Montant standard d'une mensualité — calculé automatiquement mais
    // modifiable avant confirmation ; la dernière mensualité absorbe l'écart
    // d'arrondi pour que la somme des mensualités tombe exactement sur le
    // montant financé.
    public decimal InstalmentAmount { get; set; }

    public InstalmentPlanStatus Status { get; set; }

    public List<SaleLine> Lines { get; set; } = new();

    public List<Instalment> Instalments { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}

public class SaleLine
{
    public int Id { get; set; }

    public int InstalmentPlanId { get; set; }

    public InstalmentPlan InstalmentPlan { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

// Une ligne = un mois d'un plan. Statut modifié par le rapprochement des
// paiements (#10) ; la référence de mois (MM/YYYY) est dérivée de l'échéance.
public class Instalment
{
    public int Id { get; set; }

    public int InstalmentPlanId { get; set; }

    public InstalmentPlan InstalmentPlan { get; set; } = null!;

    public DateTime DueDate { get; set; }

    public decimal Amount { get; set; }

    public InstalmentStatus Status { get; set; }

    public string ReferenceMonth => DueDate.ToString("MM/yyyy");

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}
