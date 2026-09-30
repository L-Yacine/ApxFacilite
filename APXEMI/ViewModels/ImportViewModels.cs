using APXEMI.Models;

namespace APXEMI.ViewModels;

// ---- Import de données initiales (.xlsx, migration) ----
// Types partagés entre LegacyImportService (analyse/validation/application),
// ImportController et les vues Import/*.

public class ImportErrorItem
{
    public string Sheet { get; set; } = string.Empty;

    // 0 = niveau en-têtes / fichier ; sinon numéro de ligne Excel (>= 2).
    public int RowNumber { get; set; }

    public string Message { get; set; } = string.Empty;
}

public class ImportSheetErrorsViewModel
{
    public string Sheet { get; set; } = string.Empty;

    public List<ImportErrorItem> Items { get; set; } = new();
}

public class ParsedProductRow
{
    public int RowNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public decimal UnitCost { get; set; }

    public decimal UnitSalePrice { get; set; }

    public int InitialStock { get; set; }
}

public class ParsedSupplierRow
{
    public int RowNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }
}

public class ParsedClientRow
{
    public int RowNumber { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountKey { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? DossierNumber { get; set; }
}

public class ParsedSaleLineRow
{
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

public class ParsedInstalmentRow
{
    public DateTime DueDate { get; set; }

    public decimal Amount { get; set; }

    public InstalmentStatus Status { get; set; }
}

public class ParsedSaleRow
{
    public int RowNumber { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string ClientAccountNumber { get; set; } = string.Empty;

    public DateTime SaleDate { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal DownPayment { get; set; }

    public int NumberOfInstalments { get; set; }

    public decimal? InstalmentAmount { get; set; }

    public InstalmentPlanStatus Status { get; set; }

    public List<ParsedSaleLineRow> Lines { get; set; } = new();

    // null = aucun échéancier fourni → recalculé comme une nouvelle vente.
    public List<ParsedInstalmentRow>? Schedule { get; set; }
}

public class LegacyImportParseResult
{
    public List<ImportErrorItem> Errors { get; } = new();

    public bool HasErrors => Errors.Count > 0;

    public List<ParsedProductRow> Products { get; } = new();

    public List<ParsedSupplierRow> Suppliers { get; } = new();

    public List<ParsedClientRow> Clients { get; } = new();

    public List<ParsedSaleRow> Sales { get; } = new();

    public HashSet<string> CategoriesToCreate { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> BrandsToCreate { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LegacyImportApplyResult
{
    public int Products { get; set; }

    public int Suppliers { get; set; }

    public int Clients { get; set; }

    public int Sales { get; set; }

    public int Lines { get; set; }

    public int Instalments { get; set; }
}

public class ImportPreviewViewModel
{
    public string FileName { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public bool HasErrors { get; set; }

    public List<ImportSheetErrorsViewModel> ErrorGroups { get; set; } = new();

    public int ProductCount { get; set; }

    public int SupplierCount { get; set; }

    public int ClientCount { get; set; }

    public int SaleCount { get; set; }

    public int InstalmentCount { get; set; }

    public int CategoriesToCreate { get; set; }

    public int BrandsToCreate { get; set; }

    public int SalesWithSchedule { get; set; }

    public int SalesWithGeneratedSchedule { get; set; }

    public List<ParsedProductRow> Products { get; set; } = new();

    public List<ParsedSupplierRow> Suppliers { get; set; } = new();

    public List<ParsedClientRow> Clients { get; set; } = new();

    public List<ParsedSaleRow> Sales { get; set; } = new();
}
