using System.ComponentModel.DataAnnotations;

namespace APXEMI.ViewModels;

public class StoreSettingsViewModel
{
    [Required(ErrorMessage = "Le numéro de compte est requis.")]
    [RegularExpression(@"^\d{5,20}$",
        ErrorMessage = "Le numéro de compte doit contenir entre 5 et 20 chiffres, sans espaces ni lettres.")]
    [Display(Name = "Numéro de compte du magasin (Compte B)")]
    public string StoreAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "La clé est requise.")]
    [RegularExpression(@"^\d{1,2}$",
        ErrorMessage = "La clé doit contenir 1 ou 2 chiffres.")]
    [Display(Name = "Clé du compte (Clé B)")]
    public string StoreAccountKey { get; set; } = string.Empty;

    // Affichage uniquement
    public bool IsConfigured { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }
}
