using System.ComponentModel.DataAnnotations;
using APXEMI.Models;

namespace APXEMI.ViewModels;

public class UserListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "Le nom complet est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom complet")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le nom d'utilisateur est requis.")]
    [MaxLength(50, ErrorMessage = "Le nom d'utilisateur ne peut pas dépasser 50 caractères.")]
    [Display(Name = "Nom d'utilisateur")]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "Rôle")]
    public UserRole Role { get; set; } = UserRole.Seller;

    [Required(ErrorMessage = "Le mot de passe est requis.")]
    [MinLength(8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mot de passe")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmation du mot de passe est requise.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [Display(Name = "Confirmer le mot de passe")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class EditUserViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom complet est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom complet")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le nom d'utilisateur est requis.")]
    [MaxLength(50, ErrorMessage = "Le nom d'utilisateur ne peut pas dépasser 50 caractères.")]
    [Display(Name = "Nom d'utilisateur")]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "Rôle")]
    public UserRole Role { get; set; }

    // Affichage uniquement — l'activation se gère depuis la liste.
    public bool IsActive { get; set; }

    // Empêche un Propriétaire de modifier son propre rôle (risque de verrouillage).
    public bool IsSelf { get; set; }
}

public class ResetPasswordViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nouveau mot de passe est requis.")]
    [MinLength(8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nouveau mot de passe")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "La confirmation du mot de passe est requise.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [Display(Name = "Confirmer le mot de passe")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
