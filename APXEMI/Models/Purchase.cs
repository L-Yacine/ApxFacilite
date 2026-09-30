using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Mode de règlement d'un achat fournisseur (PRD §7.2 : chèque, avec numéro
// de chèque optionnel).
public enum PurchasePaymentMethod
{
    Cash,
    Cheque
}

public static class PurchasePaymentMethodExtensions
{
    public static string ToFrenchLabel(this PurchasePaymentMethod method) => method switch
    {
        PurchasePaymentMethod.Cash => "Espèces",
        PurchasePaymentMethod.Cheque => "Chèque",
        _ => method.ToString()
    };
}

// Achat fournisseur = entrée de stock (PRD §7.2, §8) : en-tête de facture
// (fournisseur, n° facture, date, règlement) + lignes produits. Chaque ligne
// génère automatiquement un StockMovement d'entrée à l'enregistrement.
public class Purchase
{
    public int Id { get; set; }

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; }

    public PurchasePaymentMethod PaymentMethod { get; set; }

    [MaxLength(50)]
    public string? CheckNumber { get; set; }

    public List<PurchaseLine> Lines { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}

public class PurchaseLine
{
    public int Id { get; set; }

    public int PurchaseId { get; set; }

    public Purchase Purchase { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitCost { get; set; }
}
