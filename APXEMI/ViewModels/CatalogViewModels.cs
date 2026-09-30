using System.ComponentModel.DataAnnotations;

namespace APXEMI.ViewModels;

public class CatalogIndexViewModel
{
    public List<CategoryListItemViewModel> Categories { get; set; } = new();

    public List<BrandListItemViewModel> Brands { get; set; } = new();
}

public class CategoryListItemViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int ProductCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }
}

public class BrandListItemViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int ProductCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }
}

public class CreateCategoryViewModel
{
    [Required(ErrorMessage = "Le nom de la catégorie est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom de la catégorie")]
    public string Name { get; set; } = string.Empty;
}

public class CreateBrandViewModel
{
    [Required(ErrorMessage = "Le nom de la marque est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom de la marque")]
    public string Name { get; set; } = string.Empty;
}

public class EditCategoryViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom de la catégorie est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom de la catégorie")]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }
}

public class EditBrandViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom de la marque est requis.")]
    [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
    [Display(Name = "Nom de la marque")]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedByName { get; set; }
}
