using System.Globalization;
using APXEMI.Data;
using APXEMI.Models;
using APXEMI.Services;
using APXEMI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Controllers;

// Fiche de prélèvement mensuelle (PRD §7.6, §9) : choisir un mois de référence,
// le système trouve chaque mensualité due de tous les plans actifs (aucune
// oubliée, aucune doublonnée), et l'export .xlsx reprend le compte du magasin
// (Paramètres, #3) sans ressaisie. Le lot est PERSISTÉ (PrelevementBatch, §8)
// pour garder l'historique des mois et servir de base au rapprochement (#10).
// Générer/exclure/exporter : Propriétaire uniquement (table §5) ; les Vendeurs
// consultent en lecture seule.
[Authorize]
public class PrelevementsController(EmiDbContext db, ICurrentUserService currentUser) : Controller
{
    // Historique des lots générés + formulaire de génération.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var batches = await db.PrelevementBatches.AsNoTracking()
            .Include(b => b.Lines)
            .OrderByDescending(b => b.ReferenceMonth.Substring(3))
            .ThenByDescending(b => b.ReferenceMonth.Substring(0, 2))
            .ToListAsync();

        var userIds = batches
            .SelectMany(b => new[] { b.GeneratedByUserId, b.ReconciledByUserId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var model = new PrelevementIndexViewModel
        {
            Batches = batches.Select(b => new PrelevementBatchListItemViewModel
            {
                Id = b.Id,
                ReferenceMonth = b.ReferenceMonth,
                GeneratedAtUtc = b.GeneratedAtUtc,
                GeneratedByName = b.GeneratedByUserId is { } uid
                    && userNames.TryGetValue(uid, out var name) ? name : null,
                LineCount = b.Lines.Count,
                ExcludedCount = b.Lines.Count(l => l.IsExcluded),
                PaidCount = b.Lines.Count(l => l.ReconcileStatus == InstalmentStatus.Paid),
                FailedCount = b.Lines.Count(l => l.ReconcileStatus == InstalmentStatus.Failed),
                ReconciledAtUtc = b.ReconciledAtUtc,
                ReconciledByName = b.ReconciledByUserId is { } rid
                    && userNames.TryGetValue(rid, out var rname) ? rname : null,
                TotalAmount = b.Lines.Where(l => !l.IsExcluded).Sum(l => l.Amount)
            }).ToList()
        };

        return View(model);
    }

    // Génère le lot d'un mois : une ligne par mensualité en attente des plans
    // actifs, avec instantanés (client, compte B/Clé B du magasin). Un lot par
    // mois — s'il existe déjà, on renvoie vers lui (la régénération se fait
    // depuis l'écran du lot).
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(string? referenceMonth)
    {
        if (!TryParseMonth(referenceMonth, out var monthStart, out var monthLabel))
        {
            TempData["Error"] = "Mois invalide — utilisez le format MM/yyyy (ex. 01/2027).";
            return RedirectToAction(nameof(Index));
        }

        var existing = await db.PrelevementBatches
            .FirstOrDefaultAsync(b => b.ReferenceMonth == monthLabel);
        if (existing is not null)
        {
            TempData["Error"] = $"Un lot existe déjà pour {monthLabel} — ouvrez-le pour le consulter ou le régénérer.";
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        var settings = await db.StoreSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StoreSettings.SingletonId);
        if (settings is null || string.IsNullOrWhiteSpace(settings.StoreAccountNumber))
        {
            TempData["Error"] = "Configurez d'abord le compte du magasin dans Paramètres — il sera repris automatiquement sur chaque ligne.";
            return RedirectToAction(nameof(Index));
        }

        var due = await LoadDueInstalmentsAsync(monthStart);
        if (due.Instalments.Count == 0)
        {
            TempData["Error"] = $"Aucune mensualité en attente pour {monthLabel}.";
            return RedirectToAction(nameof(Index));
        }

        var now = DateTime.UtcNow;
        var batch = new PrelevementBatch
        {
            ReferenceMonth = monthLabel,
            GeneratedAtUtc = now,
            GeneratedByUserId = currentUser.UserId,
            CreatedAtUtc = now,
            CreatedByUserId = currentUser.UserId,
            Lines = due.Instalments
                .Select(i => BuildLine(i, settings, currentUser.UserId, due.PlanRanges[i.InstalmentPlanId]))
                .ToList()
        };
        db.PrelevementBatches.Add(batch);
        await db.SaveChangesAsync();

        var carried = due.Instalments.Count(i => i.DueDate < monthStart);
        TempData["Success"] = carried > 0
            ? $"Lot généré pour {monthLabel} : {due.Instalments.Count} mensualité(s), dont {carried} reportée(s) d'un mois précédent."
            : $"Lot généré pour {monthLabel} : {due.Instalments.Count} mensualité(s) trouvée(s), aucune ressaisie.";
        return RedirectToAction(nameof(Details), new { id = batch.Id });
    }

    // Aperçu du lot : chaque ligne avec case à cocher « exclure de l'export »
    // (ex. déjà payé en espèces) — Propriétaire peut modifier, Vendeur lit.
    // Le rapprochement des paiements (#10) se fait sur ce même écran.
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var batch = await db.PrelevementBatches.AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null)
            return NotFound();

        var userIds = batch.Lines
            .Select(l => l.ReconciledByUserId)
            .Concat(new[] { batch.GeneratedByUserId, batch.ReconciledByUserId })
            .Where(uid => uid.HasValue)
            .Select(uid => uid!.Value)
            .Distinct()
            .ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var orderedLines = batch.Lines
            .OrderBy(l => l.ClientLastName)
            .ThenBy(l => l.ClientFirstName)
            .ToList();

        var model = new PrelevementDetailsViewModel
        {
            Id = batch.Id,
            ReferenceMonth = batch.ReferenceMonth,
            GeneratedAtUtc = batch.GeneratedAtUtc,
            GeneratedByName = batch.GeneratedByUserId is { } uid
                && userNames.TryGetValue(uid, out var name) ? name : null,
            ReconciledAtUtc = batch.ReconciledAtUtc,
            ReconciledByName = batch.ReconciledByUserId is { } rid
                && userNames.TryGetValue(rid, out var rname) ? rname : null,
            Lines = orderedLines.Select(l => new PrelevementLineViewModel
            {
                Id = l.Id,
                ClientAccountNumber = l.ClientAccountNumber,
                ClientAccountKey = l.ClientAccountKey,
                ClientLastName = l.ClientLastName,
                ClientFirstName = l.ClientFirstName,
                Amount = l.Amount,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                NumberOfInstalments = l.NumberOfInstalments,
                IsExcluded = l.IsExcluded,
                ReconcileStatus = l.ReconcileStatus,
                ReconciledAtUtc = l.ReconciledAtUtc,
                ReconciledByName = l.ReconciledByUserId is { } lid
                    && userNames.TryGetValue(lid, out var lname) ? lname : null
            }).ToList(),
            ReconcileInputs = orderedLines.Select(l => new ReconcileLineInputViewModel
            {
                LineId = l.Id,
                Status = l.ReconcileStatus
            }).ToList()
        };

        return View(model);
    }

    // Rapprochement des paiements (#10) : le Propriétaire marque chaque ligne
    // Payée ou Échouée selon la réponse de la banque. Payée → la mensualité
    // passe au statut Payée et le solde du plan se met à jour (calculé par le
    // système) ; Échouée → le solde est inchangé et la mensualité sera
    // reportée automatiquement dans le lot du mois suivant. Le lot est
    // « rapproché » quand TOUTES les lignes sont comptabilisées.
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReconciliation(int id, List<ReconcileLineInputViewModel>? reconcileInputs)
    {
        var batch = await db.PrelevementBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null)
            return NotFound();

        var byLine = new Dictionary<int, InstalmentStatus>();
        foreach (var input in reconcileInputs ?? new List<ReconcileLineInputViewModel>())
        {
            if (input.LineId <= 0 || byLine.ContainsKey(input.LineId))
                continue;
            var status = input.Status;
            if (status != InstalmentStatus.Paid && status != InstalmentStatus.Failed)
                status = InstalmentStatus.Pending;
            byLine[input.LineId] = status;
        }

        var instalments = await db.Instalments
            .Include(i => i.InstalmentPlan)
                .ThenInclude(p => p.Instalments)
            .Where(i => batch.Lines.Select(l => l.InstalmentId).Contains(i.Id))
            .ToListAsync();
        var instalmentById = instalments.ToDictionary(i => i.Id);

        var now = DateTime.UtcNow;
        var paid = 0;
        var failed = 0;
        var reset = 0;

        foreach (var line in batch.Lines)
        {
            if (!byLine.TryGetValue(line.Id, out var newStatus) || line.ReconcileStatus == newStatus)
                continue;
            if (!instalmentById.TryGetValue(line.InstalmentId, out var instalment))
                continue;

            line.ReconcileStatus = newStatus;
            line.ReconciledAtUtc = now;
            line.ReconciledByUserId = currentUser.UserId;
            line.UpdatedAtUtc = now;
            line.UpdatedByUserId = currentUser.UserId;

            instalment.Status = newStatus;
            instalment.UpdatedAtUtc = now;
            instalment.UpdatedByUserId = currentUser.UserId;

            if (newStatus == InstalmentStatus.Paid) paid++;
            else if (newStatus == InstalmentStatus.Failed) failed++;
            else reset++;

            // Statut du plan recalculé par le système : Soldé si toutes les
            // mensualités sont payées, En cours sinon (jamais Annulé ici).
            var plan = instalment.InstalmentPlan;
            if (plan.Status != InstalmentPlanStatus.Voided)
            {
                var planStatus = plan.Instalments.All(x => x.Status == InstalmentStatus.Paid)
                    ? InstalmentPlanStatus.Completed
                    : InstalmentPlanStatus.Active;
                if (plan.Status != planStatus)
                {
                    plan.Status = planStatus;
                    plan.UpdatedAtUtc = now;
                    plan.UpdatedByUserId = currentUser.UserId;
                }
            }
        }

        var allReconciled = batch.Lines.All(l => l.ReconcileStatus != InstalmentStatus.Pending);
        batch.ReconciledAtUtc = allReconciled ? now : null;
        batch.ReconciledByUserId = allReconciled ? currentUser.UserId : null;
        batch.UpdatedAtUtc = now;
        batch.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync();

        if (paid + failed + reset > 0)
        {
            var message = $"{paid} payée(s), {failed} échouée(s), {reset} remise(s) en attente — soldes des plans mis à jour.";
            if (allReconciled)
                message += " Lot rapproché : chaque mensualité du lot est comptabilisée.";
            TempData["Success"] = message;
        }
        else
        {
            TempData["Error"] = "Aucun changement de statut enregistré.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // Enregistre les exclusions cochées sur l'écran d'aperçu.
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExclusions(int id, int[]? excludedLineIds)
    {
        var batch = await db.PrelevementBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null)
            return NotFound();

        var excluded = new HashSet<int>(excludedLineIds ?? Array.Empty<int>());
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var line in batch.Lines)
        {
            if (line.IsExcluded != excluded.Contains(line.Id))
            {
                line.IsExcluded = excluded.Contains(line.Id);
                line.UpdatedAtUtc = now;
                line.UpdatedByUserId = currentUser.UserId;
                changed = true;
            }
        }

        if (changed)
        {
            batch.UpdatedAtUtc = now;
            batch.UpdatedByUserId = currentUser.UserId;
            await db.SaveChangesAsync();
            TempData["Success"] = "Exclusions enregistrées — l'export les respectera.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // Reconstruit les lignes du lot (nouveaux instantanés, exclusions remises
    // à zéro). Bloqué si une mensualité a déjà été rapprochée (#10).
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Owner))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Regenerate(int id)
    {
        var batch = await db.PrelevementBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null)
            return NotFound();

        var reconciled = await db.Instalments.AsNoTracking()
            .Where(i => batch.Lines.Select(l => l.InstalmentId).Contains(i.Id)
                && i.Status != InstalmentStatus.Pending)
            .AnyAsync();
        if (reconciled)
        {
            TempData["Error"] = "Ce lot contient des mensualités déjà rapprochées ou reportées — la régénération est impossible.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!TryParseMonth(batch.ReferenceMonth, out var monthStart, out var monthLabel))
        {
            TempData["Error"] = "Mois de référence invalide sur ce lot.";
            return RedirectToAction(nameof(Index));
        }

        var settings = await db.StoreSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StoreSettings.SingletonId);
        if (settings is null || string.IsNullOrWhiteSpace(settings.StoreAccountNumber))
        {
            TempData["Error"] = "Configurez d'abord le compte du magasin dans Paramètres.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var due = await LoadDueInstalmentsAsync(monthStart, excludeBatchId: batch.Id);
        if (due.Instalments.Count == 0)
        {
            TempData["Error"] = $"Aucune mensualité en attente pour {monthLabel} — le lot serait vide.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Suppression puis réinsertion en deux sauvegardes : l'index unique sur
        // InstalmentId exige que les anciennes lignes soient parties avant que
        // les nouvelles n'arrivent.
        var now = DateTime.UtcNow;
        var oldLines = batch.Lines.ToList();
        batch.Lines.Clear();
        db.PrelevementLines.RemoveRange(oldLines);
        await db.SaveChangesAsync();

        foreach (var line in due.Instalments.Select(i => BuildLine(i, settings, currentUser.UserId, due.PlanRanges[i.InstalmentPlanId])))
            batch.Lines.Add(line);
        batch.UpdatedAtUtc = now;
        batch.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Lot régénéré pour {monthLabel} : {due.Instalments.Count} mensualité(s), exclusions réinitialisées.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Export .xlsx : le fichier mensuel remis à la poste — en-têtes exactes
    // du format bancaire (CCP, Montant VO, M Rate = 0, PRLV = 1…). Lignes non
    // exclues uniquement.
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Owner))]
    public async Task<IActionResult> Export(int id)
    {
        var batch = await db.PrelevementBatches.AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null)
            return NotFound();

        var lines = batch.Lines
            .Where(l => !l.IsExcluded)
            .OrderBy(l => l.ClientLastName)
            .ThenBy(l => l.ClientFirstName)
            .ToList();

        var rows = lines.Select(l => new FicheExporter.PrelevementFicheRow(
            l.ClientAccountNumber,
            l.ClientAccountKey,
            l.ClientLastName,
            l.ClientFirstName,
            l.Amount,
            l.StoreAccountNumber,
            l.StoreAccountKey,
            l.StartDate,
            l.EndDate,
            l.NumberOfInstalments)).ToList();

        var bytes = FicheExporter.BuildPrelevementFiche(
            batch.ReferenceMonth, batch.GeneratedAtUtc.ToLocalTime(), rows);
        var fileName = $"Fiche de prélèvement - {batch.ReferenceMonth.Replace("/", "-")}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // 1re et dernière échéance d'un plan (StartDate/EndDate des lignes).
    private readonly record struct PlanDateRange(DateTime StartDate, DateTime EndDate);

    // Résultat de LoadDueInstalmentsAsync : les mensualités en attente et les
    // bornes d'échéance de chaque plan concerné (PLAN, pas du mois).
    private sealed record DueInstalments(
        IReadOnlyList<Instalment> Instalments,
        IReadOnlyDictionary<int, PlanDateRange> PlanRanges)
    {
        public int Count => Instalments.Count;
    }

    // Mensualités à inclure dans le lot d'un mois, pour les plans actifs :
    // - l'échéance du mois : statut En attente (y compris une mensualité en
    //   retard qui ne serait rattachée à aucun lot), pas déjà rattachée à un
    //   autre lot (aucune oubliée, aucune doublonnée) ;
    // - le report automatique d'un échec (#10) : mensualité ÉCHOUÉE d'un mois
    //   précédent, pas encore reprise dans un lot toujours à rapprocher.
    // `excludeBatchId` : lors d'une régénération, les lignes du lot en cours
    // sont encore en base et ne doivent pas exclure leurs propres mensualités.
    // Le Include du plan s'arrête au Client : recharger les mensualités du
    // plan depuis le lot serait cyclique (Instalment → Plan → Instalments),
    // interdit en requête sans suivi ; les bornes d'échéance sont donc
    // calculées par une requête groupée séparée.
    private async Task<DueInstalments> LoadDueInstalmentsAsync(DateTime monthStart, int? excludeBatchId = null)
    {
        var monthEnd = monthStart.AddMonths(1);
        var instalments = await db.Instalments.AsNoTracking()
            .Include(i => i.InstalmentPlan)
                .ThenInclude(p => p.Client)
            .Where(i => i.InstalmentPlan.Status == InstalmentPlanStatus.Active
                && ((i.Status == InstalmentStatus.Pending
                        && i.DueDate < monthEnd
                        && !db.PrelevementLines.Any(l => l.InstalmentId == i.Id
                            && (excludeBatchId == null || l.BatchId != excludeBatchId)))
                    || (i.Status == InstalmentStatus.Failed
                        && i.DueDate < monthStart
                        && !db.PrelevementLines.Any(l => l.InstalmentId == i.Id
                            && l.ReconcileStatus == InstalmentStatus.Pending
                            && (excludeBatchId == null || l.BatchId != excludeBatchId)))))
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.InstalmentPlan.Client.LastName)
            .ThenBy(i => i.InstalmentPlan.Client.FirstName)
            .ToListAsync();

        var planIds = instalments.Select(i => i.InstalmentPlanId).Distinct().ToList();
        var planRanges = await db.Instalments.AsNoTracking()
            .Where(i => planIds.Contains(i.InstalmentPlanId))
            .GroupBy(i => i.InstalmentPlanId)
            .Select(g => new
            {
                PlanId = g.Key,
                StartDate = g.Min(i => i.DueDate),
                EndDate = g.Max(i => i.DueDate)
            })
            .ToDictionaryAsync(g => g.PlanId, g => new PlanDateRange(g.StartDate, g.EndDate));

        return new DueInstalments(instalments, planRanges);
    }

    // Une ligne = instantané du client, de la mensualité et du compte du
    // magasin au moment de la génération. StartDate/EndDate = 1re et dernière
    // mensualité du PLAN (signification confirmée, PRD §11).
    private static PrelevementLine BuildLine(Instalment instalment, StoreSettings settings, int? createdByUserId, PlanDateRange range)
    {
        var plan = instalment.InstalmentPlan;
        var now = DateTime.UtcNow;
        return new PrelevementLine
        {
            InstalmentId = instalment.Id,
            ClientAccountNumber = plan.Client.AccountNumber,
            ClientAccountKey = plan.Client.AccountKey,
            ClientLastName = plan.Client.LastName,
            ClientFirstName = plan.Client.FirstName,
            Amount = instalment.Amount,
            StartDate = range.StartDate,
            EndDate = range.EndDate,
            StoreAccountNumber = settings.StoreAccountNumber,
            StoreAccountKey = settings.StoreAccountKey,
            NumberOfInstalments = plan.NumberOfInstalments,
            IsExcluded = false,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId
        };
    }

    // Parse « MM/yyyy » et rend le 1er du mois + le libellé normalisé.
    private static bool TryParseMonth(string? value, out DateTime monthStart, out string label)
    {
        monthStart = default;
        label = string.Empty;
        value = value?.Trim() ?? string.Empty;
        if (value.Length != 7 || value[2] != '/')
            return false;
        if (!DateTime.TryParseExact(value, "MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        monthStart = new DateTime(parsed.Year, parsed.Month, 1);
        label = monthStart.ToString("MM/yyyy");
        return true;
    }
}
