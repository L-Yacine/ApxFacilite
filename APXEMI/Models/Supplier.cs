using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Fournisseur (PRD §7.2, §8). Créé à la volée lors de la saisie d'un achat :
// le nom est proposé avec suggestions ; s'il n'existe pas encore, le
// fournisseur est enregistré avec le téléphone optionnel fourni.
public class Supplier
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }

    public List<Purchase> Purchases { get; set; } = new();
}
