using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Catégorie de produits (PRD §7.3) — liste gérée par le personnel, choisie
// dans un menu déroulant lors de la création/édition d'un produit, plus jamais
// saisie en texte libre. Une catégorie désactivée (IsActive = false) disparaît
// des listes de choix mais reste visible sur l'historique — jamais supprimée.
public class Category
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}
