using APXEMI.Models;

namespace APXEMI.Services;

// Génération du calendrier des mensualités d'une vente (PRD §7.5) — règles
// confirmées :
//  - première échéance un mois après la date de vente, puis le même quantième
//    chaque mois (« anniversaire ») ;
//  - si le quantième n'existe pas dans le mois (31 → avril), l'échéance est
//    ramenée au dernier jour du mois, puis reprend son quantième normal ;
//  - montants identiques arrondis à 2 décimales ; la dernière mensualité
//    absorbe l'écart d'arrondi (ou l'écart du montant modifié par le vendeur)
//    pour que la somme tombe exactement sur le montant financé.
public static class InstalmentSchedule
{
    public static List<Instalment> Build(InstalmentPlan plan, int? createdByUserId)
    {
        var financed = plan.TotalAmount - plan.DownPayment;
        var schedule = new List<Instalment>(plan.NumberOfInstalments);
        var now = DateTime.UtcNow;

        for (var i = 1; i <= plan.NumberOfInstalments; i++)
        {
            var month = plan.SaleDate.AddMonths(i);
            var day = Math.Min(plan.SaleDate.Day, DateTime.DaysInMonth(month.Year, month.Month));
            var amount = i == plan.NumberOfInstalments
                ? financed - plan.InstalmentAmount * (plan.NumberOfInstalments - 1)
                : plan.InstalmentAmount;

            schedule.Add(new Instalment
            {
                DueDate = new DateTime(month.Year, month.Month, day),
                Amount = amount,
                Status = InstalmentStatus.Pending,
                CreatedAtUtc = now,
                CreatedByUserId = createdByUserId
            });
        }

        return schedule;
    }
}
