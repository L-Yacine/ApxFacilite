using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Client (PRD §7.4, §8). Créé au moment d'une vente (#7) ; la fiche client
// (#8) affichera l'ensemble de ses plans, passés et en cours. Le numéro de
// compte + clé servent à la fiche de prélèvement (#9) — mêmes formats que le
// compte du magasin (Paramètres). Un client peut avoir plusieurs plans actifs,
// chacun indépendant (§6).
public class Client
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string AccountNumber { get; set; } = string.Empty;

    [MaxLength(2)]
    public string AccountKey { get; set; } = string.Empty;

    [MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    // N° du dossier papier du client (classement du magasin), reporté sur la
    // fiche client. Facultatif.
    [MaxLength(50)]
    public string? DossierNumber { get; set; }

    public List<InstalmentPlan> Plans { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }

    // Nom d'affichage « Nom Prénom », tel qu'il figurera sur les fiches (§9).
    public string FullName => $"{LastName} {FirstName}";
}
