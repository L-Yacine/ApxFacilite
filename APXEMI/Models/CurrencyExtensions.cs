using System.Globalization;

namespace APXEMI.Models;

// Monnaie au format « 150 000 DA » (DESIGN.MD §3.B) : espace comme séparateur
// de milliers, suffixe DA explicite, décimales affichées seulement si présentes.
public static class CurrencyExtensions
{
    public static string ToDa(this decimal value) =>
        value.ToString("#,##0.##", CultureInfo.InvariantCulture).Replace(',', ' ') + " DA";
}
