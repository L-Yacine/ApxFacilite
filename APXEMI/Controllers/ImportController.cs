using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APXEMI.Controllers;

// Import de données initiales (migration .xlsx) — réservé au Propriétaire.
// Flux : Index (choix du fichier) → Preview (analyse + validation ; toute
// erreur bloque l'import complet) → Confirm (insertion en une transaction,
// tout ou rien). Le fichier est déposé dans un dossier temporaire et re-analysé
// à la confirmation : l'aperçu n'est jamais l'image exacte de ce qui est écrit.
[Authorize(Roles = nameof(UserRole.Owner))]
public class ImportController(LegacyImportService importService, ICurrentUserService currentUser) : Controller
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private const string SheetConsignes = "Consignes";

    private string TempDir => Path.Combine(Path.GetTempPath(), "apxemi-imports");

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Template()
    {
        using var workbook = new XLWorkbook();

        var products = workbook.AddWorksheet("Produits");
        var suppliers = workbook.AddWorksheet("Fournisseurs");
        var clients = workbook.AddWorksheet("Clients");
        var sales = workbook.AddWorksheet("Ventes");
        var lines = workbook.AddWorksheet("Lignes de vente");
        var schedule = workbook.AddWorksheet("Échéancier");

        WriteHeaders(products, "Nom", "Catégorie", "Marque", "Prix d'achat", "Prix de vente", "Stock initial");
        WriteHeaders(suppliers, "Nom", "Téléphone");
        WriteHeaders(clients, "N° de compte", "Clé", "Nom", "Prénom", "Téléphone", "N° de dossier");
        WriteHeaders(sales, "N° vente", "N° de compte", "Date de vente", "Total", "Acompte",
            "Nombre de mensualités", "Mensualité", "Statut");
        WriteHeaders(lines, "N° vente", "Produit", "Quantité", "Prix unitaire");
        WriteHeaders(schedule, "N° vente", "Échéance", "Montant", "Statut");

        var consignes = workbook.AddWorksheet(SheetConsignes);
        consignes.Cell(1, 1).Value = "APXEMI — Import de données initiales";
        consignes.Cell(1, 1).Style.Font.Bold = true;
        consignes.Cell(3, 1).Value =
            "Complétez une feuille par type de données puis enregistrez en .xlsx. L'import est unique : toute erreur bloque tout le fichier, et un doublon (produit, fournisseur, n° de compte) déjà présent en base est refusé.";
        consignes.Cell(5, 1).Value = "• Produits (obligatoire) : Nom, Catégorie, Marque, Prix d'achat, Prix de vente, Stock initial.";
        consignes.Cell(6, 1).Value = "• Fournisseurs (facultative) : Nom, Téléphone.";
        consignes.Cell(7, 1).Value = "• Clients (facultative) : N° de compte (5 à 20 chiffres), Clé (1 à 2 chiffres), Nom, Prénom, Téléphone (0X XX XX XX XX), N° de dossier (facultatif).";
        consignes.Cell(8, 1).Value =
            "• Ventes (facultative) : N° vente (référence interne du fichier), N° de compte du client, Date de vente (JJ/MM/AAAA), Total, Acompte, Nombre de mensualités, Mensualité (facultative), Statut (« En cours » ou « Soldé »).";
        consignes.Cell(9, 1).Value =
            "• Lignes de vente : N° vente, Produit (nom exact de la feuille Produits), Quantité, Prix unitaire. La somme des lignes doit correspondre au Total de la vente.";
        consignes.Cell(10, 1).Value =
            "• Échéancier (facultative) : N° vente, Échéance (JJ/MM/AAAA), Montant, Statut (« En attente », « Payée » ou « Échouée »). Si la feuille est absente, l'échéancier est recalculé comme une nouvelle vente (toutes les mensualités « En attente »).";
        consignes.Cell(12, 1).Value =
            "Formats : montants en dinars (150 000,00 ou 150000.00), dates JJ/MM/AAAA. Une vente « Soldé » doit fournir son échéancier entièrement « Payée ».";
        consignes.Columns().AdjustToContents();
        consignes.Column(1).Width = 130;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "modele-import.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Sélectionnez un fichier .xlsx avant de continuer.";
            return RedirectToAction(nameof(Index));
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Le fichier doit être au format .xlsx (Excel).";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > MaxFileSize)
        {
            TempData["Error"] = "Le fichier dépasse la taille maximale de 10 Mo.";
            return RedirectToAction(nameof(Index));
        }

        var token = SaveUpload(file);
        try
        {
            return View(await BuildPreviewAsync(token, file.FileName));
        }
        catch
        {
            DeleteUpload(token);
            TempData["Error"] = "Impossible de lire ce fichier — vérifiez qu'il s'agit bien d'un classeur .xlsx valide.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token)
    {
        if (!Guid.TryParse(token, out _) || !System.IO.File.Exists(PathForToken(token)))
        {
            TempData["Error"] = "La session d'import a expiré — sélectionnez à nouveau le fichier.";
            return RedirectToAction(nameof(Index));
        }

        LegacyImportParseResult result;
        await using (var stream = System.IO.File.OpenRead(PathForToken(token)))
        {
            result = importService.Parse(stream);
            await importService.ValidateAsync(result);
        }

        if (result.HasErrors)
        {
            DeleteUpload(token);
            TempData["Error"] = "Le fichier ne passe plus la validation — rien n'a été enregistré. Relancez l'import.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var applied = await importService.ApplyAsync(result, currentUser.UserId);
            DeleteUpload(token);
            TempData["Success"] =
                $"Import terminé : {applied.Products} produit(s), {applied.Suppliers} fournisseur(s), " +
                $"{applied.Clients} client(s), {applied.Sales} vente(s) et {applied.Instalments} mensualité(s) créés.";
        }
        catch
        {
            DeleteUpload(token);
            TempData["Error"] = "L'import a échoué en cours d'enregistrement — rien n'a été écrit en base. Corrigez le fichier et réessayez.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<ImportPreviewViewModel> BuildPreviewAsync(string token, string fileName)
    {
        LegacyImportParseResult result;
        await using (var stream = System.IO.File.OpenRead(PathForToken(token)))
        {
            result = importService.Parse(stream);
            await importService.ValidateAsync(result);
        }

        return new ImportPreviewViewModel
        {
            FileName = fileName,
            Token = token,
            HasErrors = result.HasErrors,
            ErrorGroups = result.Errors
                .GroupBy(e => e.Sheet)
                .Select(g => new ImportSheetErrorsViewModel
                {
                    Sheet = g.Key,
                    Items = g.OrderBy(i => i.RowNumber).ToList()
                })
                .ToList(),
            ProductCount = result.Products.Count,
            SupplierCount = result.Suppliers.Count,
            ClientCount = result.Clients.Count,
            SaleCount = result.Sales.Count,
            InstalmentCount = result.Sales.Sum(s =>
                s.Schedule is { Count: > 0 } ? s.Schedule.Count : s.NumberOfInstalments),
            CategoriesToCreate = result.CategoriesToCreate.Count,
            BrandsToCreate = result.BrandsToCreate.Count,
            SalesWithSchedule = result.Sales.Count(s => s.Schedule is { Count: > 0 }),
            SalesWithGeneratedSchedule = result.Sales.Count(s => s.Schedule is null or { Count: 0 }),
            Products = result.Products,
            Suppliers = result.Suppliers,
            Clients = result.Clients,
            Sales = result.Sales
        };
    }

    private string PathForToken(string token) => Path.Combine(TempDir, token + ".xlsx");

    private string SaveUpload(IFormFile file)
    {
        Directory.CreateDirectory(TempDir);
        SweepUploads();

        var token = Guid.NewGuid().ToString("N");
        using var stream = System.IO.File.Create(PathForToken(token));
        file.CopyTo(stream);
        return token;
    }

    private void DeleteUpload(string token)
    {
        try
        {
            System.IO.File.Delete(PathForToken(token));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void SweepUploads()
    {
        try
        {
            foreach (var file in Directory.GetFiles(TempDir, "*.xlsx"))
            {
                if (System.IO.File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    System.IO.File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteHeaders(IXLWorksheet sheet, params string[] headers)
    {
        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];
        sheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor =
            XLColor.FromHtml("#EEF2FF");
        sheet.SheetView.FreezeRows(1);
        for (var col = 1; col <= headers.Length; col++)
            sheet.Column(col).Width = Math.Max(headers[col - 1].Length + 3, 14);
    }
}
