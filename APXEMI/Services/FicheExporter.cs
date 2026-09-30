using ClosedXML.Excel;

namespace APXEMI.Services;

// Export des trois « fiches » (PRD §9) en .xlsx, calqué sur les vrais modèles
// du magasin (dossier « Fiches Examples ») :
//  - Fiche de stock : Date | Nombre | Entrée (NB | Nom) | Sortie (NB | Nom) | Reste
//  - Fiche client   : carte imprimable (bloc infos + produits + échéancier +
//                     signatures), UNE fiche par plan de mensualités
//  - Fiche de prélèvement : fichier mensuel remis à la poste — en-têtes
//                     EXACTES du format attendu (CCP, Montant VO, M Rate…).
// ClosedXML est déjà une dépendance du projet (import de données initiales).
public static class FicheExporter
{
    private const string FontName = "Arial";
    private const string MoneyFormat = "#,##0.##\" DA\"";

    private static readonly XLColor TitleFill = XLColor.FromHtml("#E0E0E0");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#F0F0F0");

    // ---- Données d'entrée (les contrôleurs restent responsables des requêtes) ----

    public sealed record StockFicheRow(
        DateTime Date, int QuantityBefore, bool IsIncoming, string OperatorName, int QuantityAfter);

    public sealed record ClientFicheLine(string Article, int Quantity, decimal UnitPrice);

    public sealed record ClientFicheInstalment(DateTime DueDate, decimal Amount, string StatusLabel);

    public sealed class ClientFicheData
    {
        public required string LastName { get; init; }
        public required string FirstName { get; init; }
        public required string AccountNumber { get; init; }
        public required string AccountKey { get; init; }
        public string? Phone { get; init; }
        public string? DossierNumber { get; init; }
        public DateTime FirstInstalmentDate { get; init; }
        public DateTime LastInstalmentDate { get; init; }
        public int NumberOfInstalments { get; init; }
        public decimal InstalmentAmount { get; init; }
        public decimal AmountPaid { get; init; }
        public decimal AmountRemaining { get; init; }
        public required IReadOnlyList<ClientFicheLine> Lines { get; init; }
        public required IReadOnlyList<ClientFicheInstalment> Instalments { get; init; }
    }

    public sealed record PrelevementFicheRow(
        string AccountNumber, string AccountKey, string LastName, string FirstName,
        decimal Amount, string StoreAccountNumber, string StoreAccountKey,
        DateTime StartDate, DateTime EndDate, int NumberOfInstalments);

    // ---- Fiche de stock (un produit) ----

    public static byte[] BuildStockFiche(string productName, IReadOnlyList<StockFicheRow> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(SheetName(productName));

        double[] widths = [20, 12, 12, 20, 12, 20, 12];
        for (var c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];

        TitleRow(ws, row: 1, lastCol: 7, "Fiche de stock");
        SubtitleRow(ws, row: 2, lastCol: 7, $"Article : {productName}");
        SubtitleRow(ws, row: 3, lastCol: 7, $"Date : {DateTime.Now:dd/MM/yyyy}");

        // En-tête à deux niveaux (lignes 5-6) : Entrée et Sortie regroupent
        // chacune « NB » (quantité) et « Nom » (fournisseur / client).
        const int headerRow = 5;
        SetHeader(ws.Cell(headerRow, 1), "Date");
        SetHeader(ws.Cell(headerRow, 2), "Nombre");
        SetHeader(ws.Cell(headerRow, 3), "Entrée");
        SetHeader(ws.Cell(headerRow, 5), "Sortie");
        SetHeader(ws.Cell(headerRow, 7), "Reste");
        SetHeader(ws.Cell(headerRow + 1, 3), "NB");
        SetHeader(ws.Cell(headerRow + 1, 4), "Nom");
        SetHeader(ws.Cell(headerRow + 1, 5), "NB");
        SetHeader(ws.Cell(headerRow + 1, 6), "Nom");
        // Cellules vides coiffées par les fusions verticales : même style.
        SetHeader(ws.Cell(headerRow, 4), "");
        SetHeader(ws.Cell(headerRow, 6), "");
        SetHeader(ws.Cell(headerRow + 1, 1), "");
        SetHeader(ws.Cell(headerRow + 1, 2), "");
        SetHeader(ws.Cell(headerRow + 1, 7), "");

        ws.Range(headerRow, 3, headerRow, 4).Merge();          // Entrée
        ws.Range(headerRow, 5, headerRow, 6).Merge();          // Sortie
        ws.Range(headerRow, 1, headerRow + 1, 1).Merge();      // Date
        ws.Range(headerRow, 2, headerRow + 1, 2).Merge();      // Nombre
        ws.Range(headerRow, 7, headerRow + 1, 7).Merge();      // Reste

        var r = headerRow + 2;
        foreach (var m in rows)
        {
            var moved = Math.Abs(m.QuantityAfter - m.QuantityBefore);
            SetData(ws.Cell(r, 1), m.Date.ToString("dd/MM/yyyy"));
            SetData(ws.Cell(r, 2), m.QuantityBefore);
            SetData(ws.Cell(r, 3), m.IsIncoming ? (XLCellValue)moved : "-");
            SetData(ws.Cell(r, 4), m.IsIncoming ? m.OperatorName : "-");
            SetData(ws.Cell(r, 5), m.IsIncoming ? "-" : (XLCellValue)moved);
            SetData(ws.Cell(r, 6), m.IsIncoming ? "-" : m.OperatorName);
            SetData(ws.Cell(r, 7), m.QuantityAfter);
            r++;
        }

        return Save(workbook);
    }

    // ---- Fiche client (un plan de mensualités) ----

    public static byte[] BuildClientFiche(ClientFicheData data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(SheetName($"Fiche client - {data.LastName} {data.FirstName}"));

        double[] widths = [30, 22, 14, 26, 22];
        for (var c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];

        TitleRow(ws, row: 1, lastCol: 5, "Fiche client");

        // Bloc d'informations : deux paires libellé/valeur par ligne.
        InfoPair(ws, 3, "Date :", DateTime.Now.ToString("dd/MM/yyyy"),
            "N° de compte :", $"{data.AccountNumber}  Clé {data.AccountKey}");
        InfoPair(ws, 4, "Nom :", data.LastName,
            "N° de dossier :", data.DossierNumber ?? "—");
        InfoPair(ws, 5, "Prénom :", data.FirstName,
            "Téléphone :", FormatPhone(data.Phone) ?? "—");
        InfoPair(ws, 6, "Date de début :", data.FirstInstalmentDate.ToString("dd/MM/yyyy"),
            "Date de fin :", data.LastInstalmentDate.ToString("dd/MM/yyyy"));
        InfoPair(ws, 7, "Nombre de mensualités :", data.NumberOfInstalments.ToString(),
            "Montant de la mensualité :", null);
        SetInfoValue(ws.Cell(7, 5), data.InstalmentAmount, money: true);
        InfoPair(ws, 8, "Montant payé :", null, "Montant restant :", null);
        SetInfoValue(ws.Cell(8, 2), data.AmountPaid, money: true);
        SetInfoValue(ws.Cell(8, 5), data.AmountRemaining, money: true);

        // Produits vendus (lignes de la vente) + total.
        const int productsHeader = 10;
        SetHeader(ws.Cell(productsHeader, 1), "Article");
        SetHeader(ws.Cell(productsHeader, 2), "Quantité");
        SetHeader(ws.Cell(productsHeader, 3), "PU");

        var r = productsHeader + 1;
        foreach (var line in data.Lines)
        {
            SetData(ws.Cell(r, 1), line.Article);
            SetData(ws.Cell(r, 2), line.Quantity);
            SetData(ws.Cell(r, 3), line.UnitPrice, money: true);
            r++;
        }

        SetData(ws.Cell(r, 1), "TOTAL", bold: true);
        SetData(ws.Cell(r, 2), "");
        SetData(ws.Cell(r, 3), data.Lines.Sum(l => l.Quantity * l.UnitPrice), money: true, bold: true);
        var totalRow = r;

        // Échéancier du plan.
        var scheduleHeader = totalRow + 2;
        SetHeader(ws.Cell(scheduleHeader, 1), "Date de paiement");
        SetHeader(ws.Cell(scheduleHeader, 2), "Montant");
        SetHeader(ws.Cell(scheduleHeader, 3), "Statut");

        r = scheduleHeader + 1;
        foreach (var ins in data.Instalments)
        {
            SetData(ws.Cell(r, 1), ins.DueDate.ToString("dd/MM/yyyy"));
            SetData(ws.Cell(r, 2), ins.Amount, money: true);
            SetData(ws.Cell(r, 3), ins.StatusLabel);
            r++;
        }

        // Signatures — la fiche est imprimée puis signée par les deux parties.
        var signaturesRow = r + 2;
        SetLabel(ws.Cell(signaturesRow, 1), "Signature du gérant");
        SetLabel(ws.Cell(signaturesRow, 4), "Signature du client");

        return Save(workbook);
    }

    // ---- Fiche de prélèvement (lot mensuel, remis à la poste) ----

    public static byte[] BuildPrelevementFiche(
        string referenceMonth, DateTime generationDate, IReadOnlyList<PrelevementFicheRow> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(SheetName($"Prélèvement {referenceMonth.Replace("/", "-")}"));

        double[] widths = [5, 15, 8, 20, 20, 12, 15, 8, 15, 15, 15, 8, 12, 8, 15];
        for (var c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];

        TitleRow(ws, row: 1, lastCol: 15, $"Fiche de Prélèvement : {referenceMonth}");

        // En-têtes EXACTES du fichier attendu par la poste (ligne 3).
        string[] headers =
        [
            "#", "CCP", "Clé", "Nom", "Prénom", "Montant VO",
            "Compte B", "Clé", "Date Debut", "Date Fin",
            "Date Creation", "M Rate", "N Echeances", "PRLV", "Reference"
        ];
        const int headerRow = 3;
        for (var c = 0; c < headers.Length; c++)
            SetHeader(ws.Cell(headerRow, c + 1), headers[c]);

        var r = headerRow + 1;
        var index = 1;
        foreach (var line in rows)
        {
            SetData(ws.Cell(r, 1), index++);
            SetData(ws.Cell(r, 2), line.AccountNumber, centered: false);
            SetData(ws.Cell(r, 3), line.AccountKey, centered: false);
            SetData(ws.Cell(r, 4), line.LastName, centered: false);
            SetData(ws.Cell(r, 5), line.FirstName, centered: false);
            SetData(ws.Cell(r, 6), line.Amount);
            SetData(ws.Cell(r, 7), line.StoreAccountNumber, centered: false);
            SetData(ws.Cell(r, 8), line.StoreAccountKey, centered: false);
            SetData(ws.Cell(r, 9), line.StartDate.ToString("dd/MM/yyyy"));
            SetData(ws.Cell(r, 10), line.EndDate.ToString("dd/MM/yyyy"));
            SetData(ws.Cell(r, 11), generationDate.ToString("dd/MM/yyyy"));
            SetData(ws.Cell(r, 12), 0); // M Rate : valeur fixe du format bancaire
            SetData(ws.Cell(r, 13), line.NumberOfInstalments);
            SetData(ws.Cell(r, 14), 1); // PRLV : valeur fixe du format bancaire
            SetData(ws.Cell(r, 15), referenceMonth, centered: false);
            r++;
        }

        return Save(workbook);
    }

    // ---- Styles et helpers internes ----

    private static void TitleRow(IXLWorksheet ws, int row, int lastCol, string text)
    {
        var range = ws.Range(row, 1, row, lastCol);
        range.Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 14;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Fill.BackgroundColor = TitleFill;
    }

    private static void SubtitleRow(IXLWorksheet ws, int row, int lastCol, string text)
    {
        var range = ws.Range(row, 1, row, lastCol);
        range.Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 12;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static void InfoPair(
        IXLWorksheet ws, int row, string labelA, string? valueA, string labelD, string? valueD)
    {
        SetLabel(ws.Cell(row, 1), labelA);
        SetInfoValue(ws.Cell(row, 2), valueA ?? string.Empty, money: false);
        SetLabel(ws.Cell(row, 4), labelD);
        SetInfoValue(ws.Cell(row, 5), valueD ?? string.Empty, money: false);
    }

    private static void SetLabel(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 11;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetInfoValue(IXLCell cell, XLCellValue value, bool money)
    {
        cell.Value = value;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 11;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (money)
            cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void SetHeader(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 11;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Fill.BackgroundColor = HeaderFill;
        SetBorder(cell, XLBorderStyleValues.Medium);
    }

    private static void SetData(
        IXLCell cell, XLCellValue value, bool money = false, bool bold = false, bool centered = true)
    {
        cell.Value = value;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = 11;
        cell.Style.Font.Bold = bold;
        cell.Style.Alignment.Horizontal = centered
            ? XLAlignmentHorizontalValues.Center
            : XLAlignmentHorizontalValues.Left;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (money)
            cell.Style.NumberFormat.Format = MoneyFormat;
        SetBorder(cell, XLBorderStyleValues.Thin);
    }

    private static void SetBorder(IXLCell cell, XLBorderStyleValues style)
    {
        cell.Style.Border.TopBorder = style;
        cell.Style.Border.BottomBorder = style;
        cell.Style.Border.LeftBorder = style;
        cell.Style.Border.RightBorder = style;
        cell.Style.Border.TopBorderColor = XLColor.Black;
        cell.Style.Border.BottomBorderColor = XLColor.Black;
        cell.Style.Border.LeftBorderColor = XLColor.Black;
        cell.Style.Border.RightBorderColor = XLColor.Black;
    }

    private static string? FormatPhone(string? phone) =>
        phone is { Length: 10 } p ? $"{p[..2]} {p[2..4]} {p[4..6]} {p[6..8]} {p[8..]}" : phone;

    // Excel refuse les noms de feuille de plus de 31 caractères et certains symboles.
    private static string SheetName(string value)
    {
        char[] invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var cleaned = string.Concat(value.Select(ch => invalid.Contains(ch) ? ' ' : ch)).Trim();
        if (cleaned.Length == 0)
            cleaned = "Fiche";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31].TrimEnd();
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
