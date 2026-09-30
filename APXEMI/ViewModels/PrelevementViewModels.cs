using APXEMI.Models;

namespace APXEMI.ViewModels;

// --- Fiche de prélèvement mensuelle (PRD §7.6, §9) ---

public class PrelevementIndexViewModel
{
    // Mois proposé par défaut : le mois suivant (le prélèvement du mois N est
    // déposé avant, pour prélever sur le compte du client au mois N).
    public string ReferenceMonth { get; set; } = DateTime.Now.AddMonths(1).ToString("MM/yyyy");

    public List<PrelevementBatchListItemViewModel> Batches { get; set; } = new();
}

public class PrelevementBatchListItemViewModel
{
    public int Id { get; set; }

    public string ReferenceMonth { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    public string? GeneratedByName { get; set; }

    public int LineCount { get; set; }

    public int ExcludedCount { get; set; }

    public decimal TotalAmount { get; set; }

    // Rapprochement (#10) : avancement du lot dans la liste.
    public int PaidCount { get; set; }

    public int FailedCount { get; set; }

    public DateTime? ReconciledAtUtc { get; set; }

    public string? ReconciledByName { get; set; }
}

public class PrelevementDetailsViewModel
{
    public int Id { get; set; }

    public string ReferenceMonth { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    public string? GeneratedByName { get; set; }

    public DateTime? ReconciledAtUtc { get; set; }

    public string? ReconciledByName { get; set; }

    public List<PrelevementLineViewModel> Lines { get; set; } = new();

    // État actuel des lignes, alimentant le formulaire de rapprochement (#10).
    public List<ReconcileLineInputViewModel> ReconcileInputs { get; set; } = new();
}

public class PrelevementLineViewModel
{
    public int Id { get; set; }

    public string ClientAccountNumber { get; set; } = string.Empty;

    public string ClientAccountKey { get; set; } = string.Empty;

    public string ClientLastName { get; set; } = string.Empty;

    public string ClientFirstName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public int NumberOfInstalments { get; set; }

    public bool IsExcluded { get; set; }

    // Statut de rapprochement (#10) : En attente / Payée / Échouée.
    public InstalmentStatus ReconcileStatus { get; set; } = InstalmentStatus.Pending;

    public DateTime? ReconciledAtUtc { get; set; }

    public string? ReconciledByName { get; set; }
}

// Ligne saisie dans le formulaire de rapprochement (#10) : l'identifiant de la
// ligne du lot et le statut choisi (En attente / Payée / Échouée).
public class ReconcileLineInputViewModel
{
    public int LineId { get; set; }

    public InstalmentStatus Status { get; set; } = InstalmentStatus.Pending;
}
