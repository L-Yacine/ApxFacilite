using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Compte bancaire/CCP du magasin, réutilisé comme « Compte B / Clé B » sur
// chaque ligne de la fiche de prélèvement (PRD §8, §9). Ligne unique (Id = 1),
// créée à la première sauvegarde puis mise à jour — jamais supprimée.
public class StoreSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    [MaxLength(20)]
    public string StoreAccountNumber { get; set; } = string.Empty;

    [MaxLength(2)]
    public string StoreAccountKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}
