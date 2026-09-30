using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Catalogue produit (PRD §7.3, §8). Un produit démarre toujours avec un stock
// de 0 ; CurrentStockQuantity est calculé par le système à partir des mouvements
// (achats/ventes, issues #5/#7) — jamais modifié à la main.
// Un produit retiré (IsActive = false) n'est plus proposé lors des nouveaux
// achats/ventes mais reste visible sur l'historique — jamais supprimé.
// Catégorie et marque sont des références vers des listes gérées (menu
// déroulant), plus jamais de texte libre.
public class Product
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public int BrandId { get; set; }

    public Brand? Brand { get; set; }

    public decimal UnitCost { get; set; }

    public decimal UnitSalePrice { get; set; }

    // Dérivé du grand livre des mouvements — en lecture seule partout dans l'UI.
    public int CurrentStockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}
