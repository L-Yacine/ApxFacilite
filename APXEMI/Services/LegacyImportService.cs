using System.Globalization;
using System.Text.RegularExpressions;
using APXEMI.Data;
using APXEMI.Models;
using APXEMI.ViewModels;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Services;

// Import de données initiales (migration) depuis un classeur .xlsx — le
// magasin fournit son catalogue, ses fournisseurs, ses clients et ses plans
// de mensualités existants pour ne rien ressaisir à la main.
// Règles confirmées avec le Propriétaire :
//  - import unique et complet : toute erreur bloque TOUT le fichier, jamais
//    d'import partiel ; un doublon (produit, fournisseur, compte client) déjà
//    présent en base ou dans le fichier est une erreur ;
//  - les plans « Soldé » s'importent tels quels (échéancier entièrement payé) ;
//  - les invariants du domaine restent intacts : le stock initial est écrit
//    comme un mouvement « Entrée » du grand livre (jamais de saisie manuelle
//    de quantité) et les soldes des plans se recalculent depuis le statut des
//    mensualités importées.
// Format du classeur (feuilles nommées, en-têtes en première ligne) :
//  Produits (obligatoire) : Nom, Catégorie, Marque, Prix d'achat, Prix de
//      vente, Stock initial
//  Fournisseurs (facultative) : Nom, Téléphone
//  Clients (facultative) : N° de compte, Clé, Nom, Prénom, Téléphone,
//      N° de dossier (facultatif)
//  Ventes (facultative) : N° vente, N° de compte, Date de vente, Total,
//      Acompte, Nombre de mensualités, Mensualité, Statut
//  Lignes de vente : N° vente, Produit, Quantité, Prix unitaire
//  Échéancier (facultative) : N° vente, Échéance, Montant, Statut
// Si la feuille « Échéancier » est absente, le calendrier est recalculé comme
// lors d'une nouvelle vente (toutes les mensualités « En attente »).
public class LegacyImportService(EmiDbContext db)
{
    private const string SheetProducts = "Produits";
    private const string SheetSuppliers = "Fournisseurs";
    private const string SheetClients = "Clients";
    private const string SheetSales = "Ventes";
    private const string SheetSaleLines = "Lignes de vente";
    private const string SheetSchedule = "Échéancier";

    private static readonly Regex AccountNumberRegex = new(@"^\d{5,20}$");
    private static readonly Regex AccountKeyRegex = new(@"^\d{1,2}$");
    private static readonly Regex PhoneRegex = new(@"^0\d{9}$");
    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd"];

    // ---------- Analyse + validation interne du fichier ----------

    public LegacyImportParseResult Parse(Stream stream)
    {
        var result = new LegacyImportParseResult();
        using var workbook = new XLWorkbook(stream);

        var productsWs = FindSheet(workbook, SheetProducts);
        var suppliersWs = FindSheet(workbook, SheetSuppliers);
        var clientsWs = FindSheet(workbook, SheetClients);
        var salesWs = FindSheet(workbook, SheetSales);
        var linesWs = FindSheet(workbook, SheetSaleLines);
        var scheduleWs = FindSheet(workbook, SheetSchedule);

        if (productsWs is null)
            result.Errors.Add(new ImportErrorItem
            {
                Sheet = "Fichier", RowNumber = 0,
                Message = $"La feuille « {SheetProducts} » est obligatoire."
            });

        if (productsWs is not null) ParseProducts(productsWs, result);
        if (suppliersWs is not null) ParseSuppliers(suppliersWs, result);
        if (clientsWs is not null) ParseClients(clientsWs, result);
        if (salesWs is not null) ParseSales(salesWs, result);
        if (linesWs is not null) ParseSaleLines(linesWs, result);
        if (scheduleWs is not null) ParseSchedule(scheduleWs, result);

        ValidateSalesCrossChecks(result);

        if (!result.HasErrors && result.Products.Count == 0 && result.Suppliers.Count == 0
            && result.Clients.Count == 0 && result.Sales.Count == 0)
        {
            result.Errors.Add(new ImportErrorItem
            {
                Sheet = "Fichier", RowNumber = 0,
                Message = "Le fichier ne contient aucune donnée à importer."
            });
        }

        return result;
    }

    // Vérifications contre la base existante (doublons, références croisées).
    public async Task ValidateAsync(LegacyImportParseResult result)
    {
        var existingProducts = (await db.Products.AsNoTracking().Select(p => p.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSuppliers = (await db.Suppliers.AsNoTracking().Select(s => s.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingAccounts = (await db.Clients.AsNoTracking().Select(c => c.AccountNumber).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var existingCategories = (await db.Categories.AsNoTracking().Select(c => c.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingBrands = (await db.Brands.AsNoTracking().Select(b => b.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var p in result.Products)
        {
            if (existingProducts.Contains(p.Name))
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = SheetProducts, RowNumber = p.RowNumber,
                    Message = $"Le produit « {p.Name} » existe déjà dans le catalogue."
                });
            if (!existingCategories.Contains(p.CategoryName))
                result.CategoriesToCreate.Add(p.CategoryName);
            if (!existingBrands.Contains(p.BrandName))
                result.BrandsToCreate.Add(p.BrandName);
        }

        foreach (var s in result.Suppliers)
        {
            if (existingSuppliers.Contains(s.Name))
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = SheetSuppliers, RowNumber = s.RowNumber,
                    Message = $"Le fournisseur « {s.Name} » existe déjà."
                });
        }

        foreach (var c in result.Clients)
        {
            if (existingAccounts.Contains(c.AccountNumber))
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = SheetClients, RowNumber = c.RowNumber,
                    Message = $"Un client existe déjà avec le compte {c.AccountNumber}."
                });
        }

        foreach (var sale in result.Sales)
        {
            var inFile = result.Clients.Any(cl =>
                string.Equals(cl.AccountNumber, sale.ClientAccountNumber, StringComparison.Ordinal));
            if (!inFile && !existingAccounts.Contains(sale.ClientAccountNumber))
            {
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = SheetSales, RowNumber = sale.RowNumber,
                    Message = $"N° de compte {sale.ClientAccountNumber} inconnu — ajoutez ce client dans la feuille « {SheetClients} »."
                });
            }
        }
    }

    // ---------- Application (une transaction, tout ou rien) ----------

    public async Task<LegacyImportApplyResult> ApplyAsync(LegacyImportParseResult result, int? userId)
    {
        var now = DateTime.UtcNow;
        var counts = new LegacyImportApplyResult();

        await using var tx = await db.Database.BeginTransactionAsync();

        var categories = (await db.Categories.ToListAsync())
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var brands = (await db.Brands.ToListAsync())
            .ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var clientsByAccount = (await db.Clients.ToListAsync())
            .ToDictionary(c => c.AccountNumber, StringComparer.Ordinal);

        foreach (var name in result.CategoriesToCreate)
        {
            var category = new Category
            {
                Name = name, IsActive = true, CreatedAtUtc = now, CreatedByUserId = userId
            };
            db.Categories.Add(category);
            categories[name] = category;
        }
        foreach (var name in result.BrandsToCreate)
        {
            var brand = new Brand
            {
                Name = name, IsActive = true, CreatedAtUtc = now, CreatedByUserId = userId
            };
            db.Brands.Add(brand);
            brands[name] = brand;
        }

        var newProducts = new List<Product>();
        foreach (var p in result.Products)
        {
            var product = new Product
            {
                Name = p.Name,
                Category = categories[p.CategoryName],
                Brand = brands[p.BrandName],
                UnitCost = p.UnitCost,
                UnitSalePrice = p.UnitSalePrice,
                CurrentStockQuantity = p.InitialStock,
                IsActive = true,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };
            db.Products.Add(product);
            newProducts.Add(product);
            counts.Products++;

            // Le stock initial entre dans le grand livre comme une entrée
            // (quantité avant 0 → après N) — jamais de quantité saisie à la main.
            if (p.InitialStock > 0)
            {
                db.StockMovements.Add(new StockMovement
                {
                    Product = product,
                    MovementDate = now,
                    QuantityBefore = 0,
                    Type = StockMovementType.In,
                    OperatorName = "Import initial",
                    QuantityAfter = p.InitialStock,
                    Observation = "Stock initial (importation)",
                    CreatedAtUtc = now,
                    CreatedByUserId = userId
                });
            }
        }
        var productsByName = newProducts.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var s in result.Suppliers)
        {
            db.Suppliers.Add(new Supplier
            {
                Name = s.Name, Phone = s.Phone, CreatedAtUtc = now, CreatedByUserId = userId
            });
            counts.Suppliers++;
        }

        foreach (var c in result.Clients)
        {
            var client = new Client
            {
                AccountNumber = c.AccountNumber,
                AccountKey = c.AccountKey,
                LastName = c.LastName,
                FirstName = c.FirstName,
                Phone = c.Phone,
                DossierNumber = c.DossierNumber,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };
            db.Clients.Add(client);
            clientsByAccount[c.AccountNumber] = client;
            counts.Clients++;
        }

        foreach (var sale in result.Sales)
        {
            var client = clientsByAccount[sale.ClientAccountNumber];
            var financed = sale.TotalAmount - sale.DownPayment;
            var standardAmount = sale.InstalmentAmount
                ?? Math.Round(financed / sale.NumberOfInstalments, 2);

            var plan = new InstalmentPlan
            {
                Client = client,
                SaleDate = sale.SaleDate,
                TotalAmount = sale.TotalAmount,
                DownPayment = sale.DownPayment,
                NumberOfInstalments = sale.NumberOfInstalments,
                InstalmentAmount = standardAmount,
                Status = sale.Status,
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };
            db.InstalmentPlans.Add(plan);

            foreach (var line in sale.Lines)
            {
                plan.Lines.Add(new SaleLine
                {
                    Product = productsByName[line.ProductName],
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
                counts.Lines++;
            }

            if (sale.Schedule is { Count: > 0 } schedule)
            {
                foreach (var ins in schedule)
                {
                    plan.Instalments.Add(new Instalment
                    {
                        DueDate = ins.DueDate,
                        Amount = ins.Amount,
                        Status = ins.Status,
                        CreatedAtUtc = now,
                        CreatedByUserId = userId
                    });
                    counts.Instalments++;
                }
            }
            else
            {
                // Pas d'échéancier fourni → calendrier recalculé comme une vente.
                plan.Instalments.AddRange(InstalmentSchedule.Build(plan, userId));
                counts.Instalments += plan.NumberOfInstalments;
            }

            counts.Sales++;
        }

        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return counts;
    }

    // ---------- Analyse feuille par feuille ----------

    private void ParseProducts(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetProducts);
        RequireHeaders(map, result, SheetProducts,
            "Nom", "Catégorie", "Marque", "Prix d'achat", "Prix de vente", "Stock initial");
        if (map.Count == 0)
            return;

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetProducts, RowNumber = row, Message = message
            });

            var name = CellText(ws, row, map, "Nom");
            var category = CellText(ws, row, map, "Catégorie");
            var brand = CellText(ws, row, map, "Marque");
            var valid = true;

            if (string.IsNullOrEmpty(name))
            {
                Err("Le nom du produit est requis.");
                valid = false;
            }
            else if (name.Length > 150)
            {
                Err("Le nom ne peut pas dépasser 150 caractères.");
                valid = false;
            }
            else if (seen.TryGetValue(name, out var firstRow))
            {
                Err($"Le produit « {name} » apparaît déjà à la ligne {firstRow}.");
                valid = false;
            }
            else
            {
                seen[name] = row;
            }

            if (string.IsNullOrEmpty(category))
            {
                Err("La catégorie est requise.");
                valid = false;
            }
            else if (category.Length > 100)
            {
                Err("La catégorie ne peut pas dépasser 100 caractères.");
                valid = false;
            }

            if (string.IsNullOrEmpty(brand))
            {
                Err("La marque est requise.");
                valid = false;
            }
            else if (brand.Length > 100)
            {
                Err("La marque ne peut pas dépasser 100 caractères.");
                valid = false;
            }

            if (!TryMoney(ws, row, map, "Prix d'achat", out var cost) || cost < 0)
            {
                Err("Prix d'achat invalide (montant positif ou nul).");
                valid = false;
            }
            if (!TryMoney(ws, row, map, "Prix de vente", out var price) || price <= 0)
            {
                Err("Prix de vente invalide (montant strictement positif).");
                valid = false;
            }
            if (!TryInt(ws, row, map, "Stock initial", out var stock) || stock < 0)
            {
                Err("Stock initial invalide (nombre entier positif ou nul).");
                valid = false;
            }

            if (valid)
            {
                result.Products.Add(new ParsedProductRow
                {
                    RowNumber = row,
                    Name = name!,
                    CategoryName = category!,
                    BrandName = brand!,
                    UnitCost = Math.Round(cost, 2),
                    UnitSalePrice = Math.Round(price, 2),
                    InitialStock = stock
                });
            }
        }
    }

    private void ParseSuppliers(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetSuppliers);
        RequireHeaders(map, result, SheetSuppliers, "Nom");
        if (map.Count == 0)
            return;

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetSuppliers, RowNumber = row, Message = message
            });

            var name = CellText(ws, row, map, "Nom");
            var phone = CellText(ws, row, map, "Téléphone");
            var valid = true;

            if (string.IsNullOrEmpty(name))
            {
                Err("Le nom du fournisseur est requis.");
                valid = false;
            }
            else if (name.Length > 150)
            {
                Err("Le nom ne peut pas dépasser 150 caractères.");
                valid = false;
            }
            else if (seen.TryGetValue(name, out var firstRow))
            {
                Err($"Le fournisseur « {name} » apparaît déjà à la ligne {firstRow}.");
                valid = false;
            }
            else
            {
                seen[name] = row;
            }

            if (phone is not null && phone.Length > 20)
            {
                Err("Le téléphone ne peut pas dépasser 20 caractères.");
                valid = false;
            }

            if (valid)
            {
                result.Suppliers.Add(new ParsedSupplierRow
                {
                    RowNumber = row, Name = name!, Phone = phone
                });
            }
        }
    }

    private void ParseClients(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetClients);
        RequireHeaders(map, result, SheetClients, "N° de compte", "Clé", "Nom", "Prénom");
        if (map.Count == 0)
            return;

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetClients, RowNumber = row, Message = message
            });

            var account = CellText(ws, row, map, "N° de compte");
            var key = CellText(ws, row, map, "Clé");
            var lastName = CellText(ws, row, map, "Nom");
            var firstName = CellText(ws, row, map, "Prénom");
            var phone = CellText(ws, row, map, "Téléphone")?.Replace(" ", string.Empty);
            var dossier = CellText(ws, row, map, "N° de dossier");
            var valid = true;

            if (string.IsNullOrEmpty(account))
            {
                Err("Le n° de compte est requis.");
                valid = false;
            }
            else if (!AccountNumberRegex.IsMatch(account))
            {
                Err("N° de compte invalide (5 à 20 chiffres, sans espaces).");
                valid = false;
            }
            else if (seen.TryGetValue(account, out var firstRow))
            {
                Err($"Le compte {account} apparaît déjà à la ligne {firstRow}.");
                valid = false;
            }
            else
            {
                seen[account] = row;
            }

            if (string.IsNullOrEmpty(key))
            {
                Err("La clé est requise.");
                valid = false;
            }
            else if (!AccountKeyRegex.IsMatch(key))
            {
                Err("Clé invalide (1 à 2 chiffres).");
                valid = false;
            }

            if (string.IsNullOrEmpty(lastName))
            {
                Err("Le nom est requis.");
                valid = false;
            }
            else if (lastName.Length > 50)
            {
                Err("Le nom ne peut pas dépasser 50 caractères.");
                valid = false;
            }

            if (string.IsNullOrEmpty(firstName))
            {
                Err("Le prénom est requis.");
                valid = false;
            }
            else if (firstName.Length > 50)
            {
                Err("Le prénom ne peut pas dépasser 50 caractères.");
                valid = false;
            }

            if (phone is not null && !PhoneRegex.IsMatch(phone))
            {
                Err("Téléphone invalide (format 0X XX XX XX XX).");
                valid = false;
            }

            if (dossier is { Length: > 50 })
            {
                Err("Le n° de dossier ne peut pas dépasser 50 caractères.");
                valid = false;
            }

            if (valid)
            {
                result.Clients.Add(new ParsedClientRow
                {
                    RowNumber = row,
                    AccountNumber = account!,
                    AccountKey = key!,
                    LastName = lastName!,
                    FirstName = firstName!,
                    Phone = phone,
                    DossierNumber = dossier
                });
            }
        }
    }

    private void ParseSales(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetSales);
        RequireHeaders(map, result, SheetSales,
            "N° vente", "N° de compte", "Date de vente", "Total", "Acompte",
            "Nombre de mensualités");
        if (map.Count == 0)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetSales, RowNumber = row, Message = message
            });

            var reference = CellText(ws, row, map, "N° vente");
            var account = CellText(ws, row, map, "N° de compte");
            var valid = true;

            if (string.IsNullOrEmpty(reference))
            {
                Err("Le n° vente est requis.");
                valid = false;
            }
            else if (reference.Length > 50)
            {
                Err("Le n° vente ne peut pas dépasser 50 caractères.");
                valid = false;
            }
            else if (!seen.Add(reference))
            {
                Err($"Le n° vente « {reference} » apparaît plusieurs fois dans le fichier.");
                valid = false;
            }

            if (string.IsNullOrEmpty(account))
            {
                Err("Le n° de compte est requis.");
                valid = false;
            }
            else if (!AccountNumberRegex.IsMatch(account))
            {
                Err("N° de compte invalide (5 à 20 chiffres, sans espaces).");
                valid = false;
            }

            if (!TryDate(ws, row, map, "Date de vente", out var saleDate))
            {
                Err("Date de vente invalide (format JJ/MM/AAAA).");
                valid = false;
            }
            else if (saleDate.Date > DateTime.Today)
            {
                Err("La date de vente ne peut pas être dans le futur.");
                valid = false;
            }

            if (!TryMoney(ws, row, map, "Total", out var total) || total <= 0)
            {
                Err("Total invalide (montant strictement positif).");
                valid = false;
            }
            else
            {
                total = Math.Round(total, 2);
            }

            var downPayment = 0m;
            if (CellText(ws, row, map, "Acompte") is not null)
            {
                if (!TryMoney(ws, row, map, "Acompte", out downPayment) || downPayment < 0)
                {
                    Err("Acompte invalide (montant positif ou nul).");
                    valid = false;
                }
                else if (downPayment > total)
                {
                    Err("L'acompte ne peut pas dépasser le montant total de la vente.");
                    valid = false;
                }
                else
                {
                    downPayment = Math.Round(downPayment, 2);
                }
            }

            if (!TryInt(ws, row, map, "Nombre de mensualités", out var count))
            {
                Err("Nombre de mensualités invalide (nombre entier).");
                valid = false;
            }
            else if (count < 1 || count > 120)
            {
                Err("Nombre de mensualités invalide (entre 1 et 120).");
                valid = false;
            }

            var status = InstalmentPlanStatus.Active;
            if (CellText(ws, row, map, "Statut") is { } statusText)
            {
                if (!TryPlanStatus(statusText, out status))
                {
                    Err("Statut invalide — valeurs admises : « En cours », « Soldé ».");
                    valid = false;
                }
            }

            decimal? instalmentAmount = null;
            if (CellText(ws, row, map, "Mensualité") is not null)
            {
                if (!TryMoney(ws, row, map, "Mensualité", out var amount) || amount <= 0)
                {
                    Err("Mensualité invalide (montant strictement positif).");
                    valid = false;
                }
                else if (count > 0 && amount * (count - 1) >= total - downPayment)
                {
                    Err("La mensualité est trop élevée — la dernière mensualité serait négative.");
                    valid = false;
                }
                else
                {
                    instalmentAmount = Math.Round(amount, 2);
                }
            }

            if (valid && saleDate != default && total > 0 && count > 0)
            {
                result.Sales.Add(new ParsedSaleRow
                {
                    RowNumber = row,
                    Reference = reference!,
                    ClientAccountNumber = account!,
                    SaleDate = saleDate.Date,
                    TotalAmount = total,
                    DownPayment = downPayment,
                    NumberOfInstalments = count,
                    InstalmentAmount = instalmentAmount,
                    Status = status
                });
            }
        }
    }

    private void ParseSaleLines(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetSaleLines);
        RequireHeaders(map, result, SheetSaleLines, "N° vente", "Produit", "Quantité", "Prix unitaire");
        if (map.Count == 0)
            return;

        var salesByRef = result.Sales.ToDictionary(s => s.Reference, StringComparer.OrdinalIgnoreCase);
        var productNames = result.Products.Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetSaleLines, RowNumber = row, Message = message
            });

            var reference = CellText(ws, row, map, "N° vente");
            var productName = CellText(ws, row, map, "Produit");
            var valid = true;
            ParsedSaleRow? sale = null;

            if (string.IsNullOrEmpty(reference) || !salesByRef.TryGetValue(reference, out sale))
            {
                Err("N° vente inconnu dans la feuille « Ventes ».");
                valid = false;
            }

            if (string.IsNullOrEmpty(productName))
            {
                Err("Le produit est requis.");
                valid = false;
            }
            else if (!productNames.Contains(productName))
            {
                Err($"Produit « {productName} » inconnu dans la feuille « {SheetProducts} ».");
                valid = false;
            }
            else if (sale is not null && sale.Lines.Any(l =>
                string.Equals(l.ProductName, productName, StringComparison.OrdinalIgnoreCase)))
            {
                Err($"Le produit « {productName} » apparaît plusieurs fois pour la vente « {sale.Reference} ».");
                valid = false;
            }

            if (!TryInt(ws, row, map, "Quantité", out var quantity) || quantity < 1)
            {
                Err("Quantité invalide (nombre entier ≥ 1).");
                valid = false;
            }

            if (!TryMoney(ws, row, map, "Prix unitaire", out var unitPrice) || unitPrice <= 0)
            {
                Err("Prix unitaire invalide (montant strictement positif).");
                valid = false;
            }

            if (valid && sale is not null)
            {
                sale.Lines.Add(new ParsedSaleLineRow
                {
                    ProductName = productName!,
                    Quantity = quantity,
                    UnitPrice = Math.Round(unitPrice, 2)
                });
            }
        }
    }

    private void ParseSchedule(IXLWorksheet ws, LegacyImportParseResult result)
    {
        var map = ReadHeaders(ws, result, SheetSchedule);
        RequireHeaders(map, result, SheetSchedule, "N° vente", "Échéance", "Montant");
        if (map.Count == 0)
            return;

        var salesByRef = result.Sales.ToDictionary(s => s.Reference, StringComparer.OrdinalIgnoreCase);
        foreach (var row in DataRows(ws))
        {
            if (!RowHasAnyContent(ws, row, map))
                continue;

            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetSchedule, RowNumber = row, Message = message
            });

            var reference = CellText(ws, row, map, "N° vente");
            var valid = true;
            ParsedSaleRow? sale = null;

            if (string.IsNullOrEmpty(reference) || !salesByRef.TryGetValue(reference, out sale))
            {
                Err("N° vente inconnu dans la feuille « Ventes ».");
                valid = false;
            }

            if (!TryDate(ws, row, map, "Échéance", out var dueDate))
            {
                Err("Échéance invalide (format JJ/MM/AAAA).");
                valid = false;
            }
            else if (sale is not null && dueDate.Date <= sale.SaleDate)
            {
                Err("L'échéance doit être postérieure à la date de vente.");
                valid = false;
            }

            if (!TryMoney(ws, row, map, "Montant", out var amount) || amount <= 0)
            {
                Err("Montant invalide (montant strictement positif).");
                valid = false;
            }

            var status = InstalmentStatus.Pending;
            if (CellText(ws, row, map, "Statut") is { } statusText)
            {
                if (!TryInstalmentStatus(statusText, out status))
                {
                    Err("Statut invalide — valeurs admises : « En attente », « Payée », « Échouée ».");
                    valid = false;
                }
            }
            if (status == InstalmentStatus.Paid && dueDate.Date > DateTime.Today)
            {
                Err("Une mensualité « Payée » ne peut pas avoir une échéance future.");
                valid = false;
            }

            if (valid && sale is not null)
            {
                sale.Schedule ??= new List<ParsedInstalmentRow>();
                sale.Schedule.Add(new ParsedInstalmentRow
                {
                    DueDate = dueDate.Date,
                    Amount = Math.Round(amount, 2),
                    Status = status
                });
            }
        }
    }

    // Vérifications croisées après lecture des ventes, lignes et échéanciers.
    private void ValidateSalesCrossChecks(LegacyImportParseResult result)
    {
        foreach (var sale in result.Sales)
        {
            void Err(string message) => result.Errors.Add(new ImportErrorItem
            {
                Sheet = SheetSales, RowNumber = sale.RowNumber, Message = message
            });

            var financed = Math.Round(sale.TotalAmount - sale.DownPayment, 2);

            if (sale.Lines.Count == 0)
            {
                Err($"Aucune ligne de vente pour « {sale.Reference} » dans la feuille « {SheetSaleLines} ».");
            }
            else
            {
                var linesSum = Math.Round(sale.Lines.Sum(l => l.Quantity * l.UnitPrice), 2);
                if (linesSum != sale.TotalAmount)
                {
                    Err($"La somme des lignes de « {sale.Reference} » ({linesSum} DA) ne correspond pas au Total ({sale.TotalAmount} DA).");
                }
            }

            if (sale.Schedule is { Count: > 0 } schedule)
            {
                if (schedule.Count != sale.NumberOfInstalments)
                {
                    Err($"L'échéancier de « {sale.Reference} » contient {schedule.Count} ligne(s) mais la vente en déclare {sale.NumberOfInstalments}.");
                }
                var scheduleSum = Math.Round(schedule.Sum(i => i.Amount), 2);
                if (scheduleSum != financed)
                {
                    Err($"La somme de l'échéancier de « {sale.Reference} » ({scheduleSum} DA) ne correspond pas au montant financé ({financed} DA).");
                }
                if (sale.Status == InstalmentPlanStatus.Completed && schedule.Any(i => i.Status != InstalmentStatus.Paid))
                {
                    Err($"Statut « Soldé » pour « {sale.Reference} » mais certaines mensualités ne sont pas « Payée ».");
                }
                if (sale.Status == InstalmentPlanStatus.Active && schedule.All(i => i.Status == InstalmentStatus.Paid))
                {
                    Err($"Toutes les mensualités de « {sale.Reference} » sont payées — indiquez le statut « Soldé ».");
                }
            }
            else if (sale.Status == InstalmentPlanStatus.Completed)
            {
                Err($"Statut « Soldé » pour « {sale.Reference} » mais aucun échéancier fourni — indiquez les mensualités payées ou passez le statut « En cours ».");
            }
        }
    }

    // ---------- Lecture des cellules ----------

    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet ws, LegacyImportParseResult result, string sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = ws.Cell(1, col).GetString().Trim();
            if (header.Length == 0)
                continue;
            if (map.ContainsKey(header))
            {
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = sheet, RowNumber = 0,
                    Message = $"L'en-tête « {header} » apparaît plusieurs fois."
                });
            }
            else
            {
                map[header] = col;
            }
        }
        return map;
    }

    private static void RequireHeaders(Dictionary<string, int> map, LegacyImportParseResult result,
        string sheet, params string[] required)
    {
        foreach (var header in required)
        {
            if (!map.ContainsKey(header))
            {
                result.Errors.Add(new ImportErrorItem
                {
                    Sheet = sheet, RowNumber = 0,
                    Message = $"Colonne « {header} » introuvable dans l'en-tête (ligne 1)."
                });
            }
        }
    }

    private static IEnumerable<int> DataRows(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 2; row <= lastRow; row++)
            yield return row;
    }

    private static bool RowHasAnyContent(IXLWorksheet ws, int row, Dictionary<string, int> map)
    {
        foreach (var col in map.Values)
        {
            if (!ws.Cell(row, col).IsEmpty())
                return true;
        }
        return false;
    }

    private static string? CellText(IXLWorksheet ws, int row, Dictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var col))
            return null;
        var cell = ws.Cell(row, col);
        return cell.IsEmpty() ? null : cell.GetString().Trim();
    }

    private static bool TryMoney(IXLWorksheet ws, int row, Dictionary<string, int> map, string header, out decimal value)
    {
        value = 0m;
        if (!map.TryGetValue(header, out var col))
            return false;
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty())
            return false;
        if (cell.DataType == XLDataType.Number)
        {
            value = (decimal)cell.GetDouble();
            return true;
        }
        if (cell.DataType == XLDataType.Text)
        {
            // Tolérance : « 150 000,50 », « 150000.50 », « 150 000 »…
            var text = cell.GetString()
                .Replace("\u00A0", string.Empty)   // espace insécable
                .Replace("\u202F", string.Empty)   // espace fine
                .Replace(" ", string.Empty);
            if (text.Contains(',') && !text.Contains('.'))
                text = text.Replace(',', '.');
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
        return false;
    }

    private static bool TryInt(IXLWorksheet ws, int row, Dictionary<string, int> map, string header, out int value)
    {
        value = 0;
        if (!TryMoney(ws, row, map, header, out var dec))
            return false;
        if (dec != Math.Truncate(dec) || dec < int.MinValue || dec > int.MaxValue)
            return false;
        value = (int)dec;
        return true;
    }

    private static bool TryDate(IXLWorksheet ws, int row, Dictionary<string, int> map, string header, out DateTime value)
    {
        value = default;
        if (!map.TryGetValue(header, out var col))
            return false;
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty())
            return false;
        if (cell.DataType == XLDataType.DateTime)
        {
            value = cell.GetDateTime();
            return true;
        }
        if (cell.DataType == XLDataType.Text)
        {
            return DateTime.TryParseExact(cell.GetString().Trim(), DateFormats,
                CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out value);
        }
        return false;
    }

    private static bool TryPlanStatus(string text, out InstalmentPlanStatus status)
    {
        status = InstalmentPlanStatus.Active;
        if (string.Equals(text, "En cours", StringComparison.OrdinalIgnoreCase))
        {
            status = InstalmentPlanStatus.Active;
            return true;
        }
        if (string.Equals(text, "Soldé", StringComparison.OrdinalIgnoreCase))
        {
            status = InstalmentPlanStatus.Completed;
            return true;
        }
        return false;
    }

    private static bool TryInstalmentStatus(string text, out InstalmentStatus status)
    {
        status = InstalmentStatus.Pending;
        if (string.Equals(text, "En attente", StringComparison.OrdinalIgnoreCase))
        {
            status = InstalmentStatus.Pending;
            return true;
        }
        if (string.Equals(text, "Payée", StringComparison.OrdinalIgnoreCase))
        {
            status = InstalmentStatus.Paid;
            return true;
        }
        if (string.Equals(text, "Échouée", StringComparison.OrdinalIgnoreCase))
        {
            status = InstalmentStatus.Failed;
            return true;
        }
        return false;
    }
}
