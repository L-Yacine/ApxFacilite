using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace APXEMI.ViewModels;

public class ProductListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public decimal UnitSalePrice { get; set; }
    public int CurrentStockQuantity { get; set; }
    public bool IsActive { get; set; }
}

public class CreateProductViewModel
{
    [Required(ErrorMessage = "Le nom / la référence est requis.")]
    [MaxLength(150, ErrorMessage = "Le nom ne peut pas dépasser 150 caractères.")]
    [Display(Name = "Nom / Référence")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Sélectionnez une catégorie.")]
    [Display(Name = "Catégorie")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Sélectionnez une marque.")]
    [Display(Name = "Marque")]
    public int BrandId { get; set; }

    [Required(ErrorMessage = "Le coût unitaire est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le coût unitaire doit être positif ou nul.")]
    [Display(Name = "Coût unitaire (DA)")]
    public decimal UnitCost { get; set; }

    [Required(ErrorMessage = "Le prix de vente est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le prix de vente doit être positif ou nul.")]
    [Display(Name = "Prix de vente (DA)")]
    public decimal UnitSalePrice { get; set; }

    // Choix issus des listes gérées — plus de saisie libre.
    public List<SelectListItem> CategoryOptions { get; set; } = new();

    public List<SelectListItem> BrandOptions { get; set; } = new();
}

public class EditProductViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom / la référence est requis.")]
    [MaxLength(150, ErrorMessage = "Le nom ne peut pas dépasser 150 caractères.")]
    [Display(Name = "Nom / Référence")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Sélectionnez une catégorie.")]
    [Display(Name = "Catégorie")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Sélectionnez une marque.")]
    [Display(Name = "Marque")]
    public int BrandId { get; set; }

    [Required(ErrorMessage = "Le coût unitaire est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le coût unitaire doit être positif ou nul.")]
    [Display(Name = "Coût unitaire (DA)")]
    public decimal UnitCost { get; set; }

    [Required(ErrorMessage = "Le prix de vente est requis.")]
    [Range(0, 999999999.99, ErrorMessage = "Le prix de vente doit être positif ou nul.")]
    [Display(Name = "Prix de vente (DA)")]
    public decimal UnitSalePrice { get; set; }

    // Affichage uniquement — stock et audit calculés par le système.
    public int CurrentStockQuantity { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }

    public List<SelectListItem> CategoryOptions { get; set; } = new();

    public List<SelectListItem> BrandOptions { get; set; } = new();
}
