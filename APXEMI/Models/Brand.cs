using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Marque de produits (PRD §7.3) — même principe que Category : liste gérée,
// choix dans un menu déroulant, jamais de saisie libre. Une marque désactivée
// disparaît des listes de choix mais reste visible sur l'historique.
public class Brand
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
