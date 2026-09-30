using System.ComponentModel.DataAnnotations;

namespace APXEMI.Models;

// Lot mensuel de prélèvements (PRD §7.6, §8) : instantané figé de ce qui a été
// exporté pour un mois de référence (« MM/yyyy », unique — un lot par mois).
// Chaque ligne pointe vers la mensualité due et copie les valeurs au moment de
// la génération, pour que l'historique du mois reste stable même si le client
// ou le plan est modifié, corrigé ou annulé plus tard (#11). Le rapprochement
// des paiements (#10) s'appuiera sur ce lot.
public class PrelevementBatch
{
    public int Id { get; set; }

    // Mois de référence, format « MM/yyyy » (ex. 01/2027).
    [MaxLength(7)]
    public string ReferenceMonth { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    public int? GeneratedByUserId { get; set; }

    // Rapprochement (#10) : date/heure où la DERNIÈRE ligne du lot a été
    // marquée Payée ou Échouée — le lot est « rapproché » quand chaque
    // mensualité est comptabilisée (aucune ne passe à la trappe).
    public DateTime? ReconciledAtUtc { get; set; }

    public int? ReconciledByUserId { get; set; }

    public List<PrelevementLine> Lines { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}

// Une ligne = une mensualité due dans le mois du lot. Le lien vers
// `Instalment` garantit « aucune oubliée, aucune doublonnée » : une
// mensualité ne peut apparaître que dans un seul lot (index unique).
public class PrelevementLine
{
    public int Id { get; set; }

    public int BatchId { get; set; }

    public PrelevementBatch Batch { get; set; } = null!;

    public int InstalmentId { get; set; }

    public Instalment Instalment { get; set; } = null!;

    // Instantanés figés à la génération (client, montant, compte B/Clé B du
    // magasin depuis Paramètres #3) — la fiche reflète l'état du jour, jamais
    // une valeur ressaisie.
    [MaxLength(20)]
    public string ClientAccountNumber { get; set; } = string.Empty;

    [MaxLength(2)]
    public string ClientAccountKey { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ClientLastName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ClientFirstName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    // Période globale du plan — 1re et dernière mensualité du plan, pas
    // seulement le mois courant (signification confirmée, PRD §11).
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [MaxLength(20)]
    public string StoreAccountNumber { get; set; } = string.Empty;

    [MaxLength(2)]
    public string StoreAccountKey { get; set; } = string.Empty;

    public int NumberOfInstalments { get; set; }

    // Exclue de l'export par le Propriétaire avant dépôt (ex. déjà payé en
    // espèces, critère #9.3) — la ligne reste en base, le rapprochement (#10)
    // en tiendra compte.
    public bool IsExcluded { get; set; }

    // Rapprochement (#10) : Payée / Échouée selon la réponse de la banque,
    // « En attente » tant que le Propriétaire n'a pas marqué la ligne. Une
    // mensualité ÉCHOUÉE est reportée automatiquement dans le lot du mois
    // suivant (nouvelle ligne) — l'index unique filtré le permet tout en
    // garantissant qu'aucune mensualité n'est reprise dans deux lots à la fois.
    public InstalmentStatus ReconcileStatus { get; set; } = InstalmentStatus.Pending;

    public DateTime? ReconciledAtUtc { get; set; }

    public int? ReconciledByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
}
