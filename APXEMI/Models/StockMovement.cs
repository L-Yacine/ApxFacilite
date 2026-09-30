using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

public enum StockMovementType
{
    In,
    Out
}

public static class StockMovementTypeExtensions
{
    public static string ToFrenchLabel(this StockMovementType type) => type switch
    {
        StockMovementType.In => "Entrée",
        StockMovementType.Out => "Sortie",
        _ => type.ToString()
    };
}

// Grand livre de stock d'un produit (PRD §7.3, §8) — miroir de la fiche de
// stock actuelle. Les quantités avant/après sont TOUJOURS calculées par le
// système à l'enregistrement de l'opération, jamais saisies à la main.
// Les lignes sont créées automatiquement par les achats (#5) et les ventes (#7).
public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    // Date métier de l'opération (date de facture pour un achat) — c'est elle
    // qui ordonne le grand livre. CreatedAtUtc reste la date de saisie réelle.
    public DateTime MovementDate { get; set; }

    public int QuantityBefore { get; set; }

    public StockMovementType Type { get; set; }

    // Nom du fournisseur (entrée) ou du client (sortie) — figé à la saisie.
    [MaxLength(150)]
    public string OperatorName { get; set; } = string.Empty;

    public int QuantityAfter { get; set; }

    [MaxLength(500)]
    public string? Observation { get; set; }

    // Lien vers l'opération d'origine (utile pour la fiche de stock #6 et
    // l'annulation #11) : un achat (entrée) ou une vente (sortie).
    public int? PurchaseId { get; set; }

    public Purchase? Purchase { get; set; }

    public int? InstalmentPlanId { get; set; }

    public InstalmentPlan? InstalmentPlan { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }
}
