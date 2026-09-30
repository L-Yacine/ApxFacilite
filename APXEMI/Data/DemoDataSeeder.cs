using APXEMI.Models;
using APXEMI.Services;
using Microsoft.EntityFrameworkCore;

namespace APXEMI.Data;

// Jeu de données de démonstration (Seed:DemoData) — inséré UNE seule fois,
// sur une base dont le catalogue est vide, dans une transaction unique, pour
// que chaque écran et le tableau de bord animent une vraie boutique
// d'électroménager en mensualités.
// Tous les invariants du domaine sont respectés :
//  - le stock ne s'écrit QUE dans le grand livre (avant/après calculés par le
//    seeder en triant les opérations par date ; CurrentStockQuantity = solde
//    final) — jamais de quantité saisie à la main ;
//  - les soldes des plans recalculent depuis le statut des mensualités ; un
//    plan n'est « Soldé » que si TOUTES ses mensualités sont « Payée » ;
//  - chaque vente est SON propre plan (plusieurs clients en ont deux) ;
//  - chaque ligne porte ses tampons d'audit (créé par / modifié par, UTC).
public static class DemoDataSeeder
{
    public static async Task SeedAsync(EmiDbContext db, int ownerId, int? sellerId)
    {
        if (await db.Categories.AnyAsync() || await db.Products.AnyAsync())
            return;

        var today = DateTime.Today;
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        // Mensualités « encaissées ce mois » : réconciliation (réponse banque)
        // en début de mois courant pour des échéances du mois précédent.
        var earlyThisMonth = today.Day >= 3 ? today.AddDays(-2) : monthStart.AddDays(1);

        await using var tx = await db.Database.BeginTransactionAsync();

        // ---------- Catégories & marques ----------

        Category Cat(string name) => new()
        {
            Name = name, IsActive = true, CreatedAtUtc = now, CreatedByUserId = ownerId
        };
        Brand Bd(string name) => new()
        {
            Name = name, IsActive = true, CreatedAtUtc = now, CreatedByUserId = ownerId
        };

        var catFridge = Cat("Réfrigérateurs");
        var catTv = Cat("Téléviseurs");
        var catWash = Cat("Machines à laver");
        var catAc = Cat("Climatiseurs");
        var catCook = Cat("Cuisinières");
        var catSmall = Cat("Petits électroménagers");
        db.Categories.AddRange(catFridge, catTv, catWash, catAc, catCook, catSmall);

        var bSamsung = Bd("Samsung");
        var bLg = Bd("LG");
        var bCondor = Bd("Condor");
        var bHaier = Bd("Haier");
        var bBeko = Bd("Beko");
        var bBrandt = Bd("Brandt");
        var bMiniMax = Bd("MiniMax");
        db.Brands.AddRange(bSamsung, bLg, bCondor, bHaier, bBeko, bBrandt, bMiniMax);

        // ---------- Produits ----------

        Product P(string name, Category cat, Brand brand, decimal cost, decimal price, bool active = true) => new()
        {
            Name = name, Category = cat, Brand = brand, UnitCost = cost, UnitSalePrice = price,
            CurrentStockQuantity = 0, IsActive = active, CreatedAtUtc = now, CreatedByUserId = ownerId
        };

        var pRefSamsung = P("Réfrigérateur Samsung 2 portes", catFridge, bSamsung, 185000m, 210000m);
        var pRefCondor = P("Réfrigérateur Condor 1 porte", catFridge, bCondor, 62000m, 72000m);
        var pTvSamsung = P("Téléviseur Samsung 55 pouces", catTv, bSamsung, 95000m, 115000m);
        var pTvLg = P("Téléviseur LG 43 pouces", catTv, bLg, 70000m, 85000m);
        var pMlLg = P("Machine à laver LG 7 kg", catWash, bLg, 78000m, 92000m);
        var pMlCondor = P("Machine à laver Condor 6 kg", catWash, bCondor, 55000m, 65000m);
        var pClimHaier = P("Climatiseur Haier 12000 BTU", catAc, bHaier, 88000m, 105000m);
        var pClimCondor = P("Climatiseur Condor 9000 BTU", catAc, bCondor, 70000m, 82000m);
        var pCuisBrandt = P("Cuisinière Brandt 5 feux", catCook, bBrandt, 90000m, 108000m);
        var pCuisBeko = P("Cuisinière Beko 4 feux", catCook, bBeko, 68000m, 80000m);
        var pMicro = P("Micro-ondes Samsung 20 L", catSmall, bSamsung, 14500m, 18000m);
        var pRobot = P("Robot mixeur MiniMax", catSmall, bMiniMax, 9500m, 12500m);
        var pTvCondor = P("Téléviseur Condor 32 pouces", catTv, bCondor, 30000m, 38000m, active: false);
        db.Products.AddRange(pRefSamsung, pRefCondor, pTvSamsung, pTvLg, pMlLg, pMlCondor,
            pClimHaier, pClimCondor, pCuisBrandt, pCuisBeko, pMicro, pRobot, pTvCondor);

        // ---------- Fournisseurs & achats (entrées de stock) ----------

        Supplier S(string name, string phone) => new()
        {
            Name = name, Phone = phone, CreatedAtUtc = now, CreatedByUserId = ownerId
        };
        var sCondor = S("Condor Électronique", "021456789");
        var sDistrib = S("Électro Distribution Alger", "023889900");
        var sBrandt = S("Brandt Algérie", "021330055");
        db.Suppliers.AddRange(sCondor, sDistrib, sBrandt);

        var events = new List<StockEvent>();

        // Un achat date d'avant la première vente de chacun de ses produits,
        // pour que le grand livre ne passe jamais en négatif.
        Purchase Achat(Supplier s, string invoice, DateTime date, PurchasePaymentMethod pay, string? check) => new()
        {
            Supplier = s, InvoiceNumber = invoice, InvoiceDate = date,
            PaymentMethod = pay, CheckNumber = check,
            CreatedAtUtc = now, CreatedByUserId = ownerId
        };
        void Entree(Purchase p, Product product, int qty)
        {
            p.Lines.Add(new PurchaseLine { Product = product, Quantity = qty, UnitCost = product.UnitCost });
            events.Add(new StockEvent(p.InvoiceDate, product, qty, StockMovementType.In,
                p.Supplier.Name, $"Achat — facture {p.InvoiceNumber}", Purchase: p, Plan: null, CreatedBy: ownerId));
        }

        var achOldDistrib = Achat(sDistrib, "EDA-10082", today.AddMonths(-11).AddDays(5), PurchasePaymentMethod.Cash, null);
        Entree(achOldDistrib, pTvSamsung, 5);
        Entree(achOldDistrib, pTvLg, 4);

        var achOldCondor = Achat(sCondor, "FC-2025-0147", today.AddMonths(-11).AddDays(8), PurchasePaymentMethod.Cheque, "CH-55210");
        Entree(achOldCondor, pTvCondor, 4);

        var achCondor = Achat(sCondor, "FC-2026-0112", today.AddMonths(-6).AddDays(-15), PurchasePaymentMethod.Cheque, "CH-88412");
        Entree(achCondor, pRefCondor, 6);
        Entree(achCondor, pMlCondor, 10);
        Entree(achCondor, pClimCondor, 8);

        var achDistrib = Achat(sDistrib, "EDA-11458", today.AddMonths(-6).AddDays(-8), PurchasePaymentMethod.Cash, null);
        Entree(achDistrib, pRefSamsung, 6);
        Entree(achDistrib, pMlLg, 8);
        Entree(achDistrib, pClimHaier, 5);
        Entree(achDistrib, pMicro, 15);
        Entree(achDistrib, pRobot, 12);

        var achBrandt = Achat(sBrandt, "BDA-2026-078", today.AddMonths(-6).AddDays(-3), PurchasePaymentMethod.Cheque, "CH-77015");
        Entree(achBrandt, pCuisBrandt, 5);
        Entree(achBrandt, pCuisBeko, 6);

        // ---------- Clients ----------

        Client C(string acct, string key, string last, string first, string phone, string dossier) => new()
        {
            AccountNumber = acct, AccountKey = key, LastName = last, FirstName = first,
            Phone = phone, DossierNumber = dossier, CreatedAtUtc = now, CreatedByUserId = ownerId
        };
        var cBennacer = C("0081254763", "12", "Bennacer", "Ahmed", "0551234567", "D-047");
        var cKaci = C("0078312560", "34", "Kaci", "Mohamed", "0662345678", "D-038");
        var cBoumediene = C("0061107234", "56", "Boumediene", "Fatima", "0773456789", "D-051");
        var cHaddad = C("0096743125", "78", "Haddad", "Yacine", "0559876543", "D-059");
        var cZerrouki = C("0055610378", "90", "Zerrouki", "Salima", "0661112233", "D-063");
        var cMessaoudi = C("0044256891", "23", "Messaoudi", "Karim", "0774445556", "D-067");
        var cBelkacem = C("0033905412", "45", "Belkacem", "Amine", "0556667778", "D-055");
        db.Clients.AddRange(cBennacer, cKaci, cBoumediene, cHaddad, cZerrouki, cMessaoudi, cBelkacem);

        // ---------- Ventes / plans de mensualités (sorties de stock) ----------

        InstalmentPlan Plan(Client client, DateTime saleDate, decimal total, decimal down, int nb, int? creator,
            params (Product Product, int Qty)[] items)
        {
            var plan = new InstalmentPlan
            {
                Client = client, SaleDate = saleDate, TotalAmount = total, DownPayment = down,
                NumberOfInstalments = nb,
                InstalmentAmount = Math.Round((total - down) / nb, 2),
                Status = InstalmentPlanStatus.Active,
                CreatedAtUtc = now, CreatedByUserId = creator
            };
            foreach (var (product, qty) in items)
            {
                plan.Lines.Add(new SaleLine { Product = product, Quantity = qty, UnitPrice = product.UnitSalePrice });
                events.Add(new StockEvent(saleDate, product, qty, StockMovementType.Out, client.FullName,
                    $"Vente — plan de {nb} mensualité(s)", Purchase: null, Plan: plan, CreatedBy: creator ?? ownerId));
            }
            plan.Instalments.AddRange(InstalmentSchedule.Build(plan, creator));
            db.InstalmentPlans.Add(plan);
            return plan;
        }

        static void MarkPaid(InstalmentPlan plan, int index, DateTime onDate, int? by)
        {
            var i = plan.Instalments[index - 1];
            i.Status = InstalmentStatus.Paid;
            i.UpdatedAtUtc = onDate;
            i.UpdatedByUserId = by;
        }

        static void MarkFailed(InstalmentPlan plan, int index, DateTime onDate, int? by)
        {
            var i = plan.Instalments[index - 1];
            i.Status = InstalmentStatus.Failed;
            i.UpdatedAtUtc = onDate;
            i.UpdatedByUserId = by;
        }

        static void Complete(InstalmentPlan plan, int by, DateTime onDate)
        {
            plan.Status = InstalmentPlanStatus.Completed;
            plan.UpdatedAtUtc = onDate;
            plan.UpdatedByUserId = by;
        }

        // P1 Bennacer — réfrigérateur Condor + TV LG, 4/8 payées.
        var p1 = Plan(cBennacer, today.AddMonths(-6).AddDays(-5), 157000m, 20000m, 8, ownerId,
            (pRefCondor, 1), (pTvLg, 1));
        for (var i = 1; i <= 4; i++)
            MarkPaid(p1, i, p1.Instalments[i - 1].DueDate.AddDays(3), ownerId);

        // P2 Kaci — TV Samsung, plan soldé (6/6 payées).
        var p2 = Plan(cKaci, today.AddMonths(-8).AddDays(-10), 115000m, 10000m, 6, ownerId, (pTvSamsung, 1));
        for (var i = 1; i <= 6; i++)
            MarkPaid(p2, i, p2.Instalments[i - 1].DueDate.AddDays(3), ownerId);
        Complete(p2, ownerId, p2.Instalments[^1].DueDate.AddDays(3));

        // P3 Boumediene — machine LG + micro-ondes : 3 payées, 1 ÉCHOUÉE le mois
        // dernier (sera reportée dans le prochain prélèvement), le reste en attente.
        var p3 = Plan(cBoumediene, today.AddMonths(-5).AddDays(-3), 110000m, 15000m, 8, ownerId,
            (pMlLg, 1), (pMicro, 1));
        for (var i = 1; i <= 3; i++)
            MarkPaid(p3, i, p3.Instalments[i - 1].DueDate.AddDays(3), ownerId);
        MarkFailed(p3, 4, p3.Instalments[3].DueDate.AddDays(3), ownerId);

        // P4 Bennacer (2e plan, invariances #6) — TV Samsung, 3/10 payées.
        var p4 = Plan(cBennacer, today.AddMonths(-4).AddDays(-3), 115000m, 15000m, 10, ownerId, (pTvSamsung, 1));
        for (var i = 1; i <= 3; i++)
            MarkPaid(p4, i, i == 3 ? earlyThisMonth : p4.Instalments[i - 1].DueDate.AddDays(3), ownerId);

        // P5 Haddad — cuisinière Beko, 2/6 payées (2e réconciliée ce mois).
        var p5 = Plan(cHaddad, today.AddMonths(-3).AddDays(-3), 80000m, 8000m, 6, ownerId, (pCuisBeko, 1));
        MarkPaid(p5, 1, p5.Instalments[0].DueDate.AddDays(3), ownerId);
        MarkPaid(p5, 2, earlyThisMonth, ownerId);

        // P6 Zerrouki — climatiseur Haier, vente fraîche du mois (vendeur),
        // 12 mensualités encore toutes en attente.
        var p6Sale = today.Day >= 4 ? today.AddDays(-3) : monthStart;
        var p6 = Plan(cZerrouki, p6Sale, 105000m, 10000m, 12, sellerId, (pClimHaier, 1));

        // P7 Messaoudi — réfrigérateur Samsung, 1/12 payée (vendeur).
        var p7 = Plan(cMessaoudi, today.AddMonths(-2).AddDays(-3), 210000m, 20000m, 12, sellerId, (pRefSamsung, 1));
        MarkPaid(p7, 1, p7.Instalments[0].DueDate.AddDays(3), sellerId);

        // P8 Belkacem — cuisinière Brandt + robot, 4/6 payées (une en retard).
        var p8 = Plan(cBelkacem, today.AddMonths(-5).AddDays(-8), 120500m, 12500m, 6, ownerId,
            (pCuisBrandt, 1), (pRobot, 1));
        for (var i = 1; i <= 4; i++)
            MarkPaid(p8, i, p8.Instalments[i - 1].DueDate.AddDays(3), ownerId);

        // P9 Kaci (2e plan) — ancienne TV 32 pouces (produit retiré), plan soldé.
        var p9 = Plan(cKaci, today.AddMonths(-10).AddDays(-12), 38000m, 2000m, 6, ownerId, (pTvCondor, 1));
        for (var i = 1; i <= 6; i++)
            MarkPaid(p9, i, p9.Instalments[i - 1].DueDate.AddDays(3), ownerId);
        Complete(p9, ownerId, p9.Instalments[^1].DueDate.AddDays(3));

        // P10 Boumediene (2e plan) — 2 micro-ondes, 1/6 payée (vendeur).
        var p10 = Plan(cBoumediene, today.AddMonths(-2).AddDays(-8), 36000m, 6000m, 6, sellerId, (pMicro, 2));
        MarkPaid(p10, 1, p10.Instalments[0].DueDate.AddDays(3), sellerId);

        // ---------- Grand livre de stock : avant/après calculés, opérations
        // triées par date pour que le solde coulant ne passe jamais en négatif.
        // ----------

        var stockByProduct = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events.OrderBy(e => e.Date))
        {
            stockByProduct.TryGetValue(e.Product.Name, out var before);
            var after = e.Type == StockMovementType.In ? before + e.Qty : before - e.Qty;
            db.StockMovements.Add(new StockMovement
            {
                Product = e.Product,
                MovementDate = e.Date,
                QuantityBefore = before,
                Type = e.Type,
                OperatorName = e.Operator,
                QuantityAfter = after,
                Observation = e.Observation,
                Purchase = e.Purchase,
                InstalmentPlan = e.Plan,
                CreatedAtUtc = now,
                CreatedByUserId = e.CreatedBy
            });
            e.Product.CurrentStockQuantity = after;
            stockByProduct[e.Product.Name] = after;
        }

        // ---------- Paramètres magasin (compte B / Clé B du prélèvement) ----------
        // Singleton (Id = 1) : s'il existe déjà (Paramètres déjà configurés),
        // on le laisse tel quel — jamais d'insertion en double.

        if (!await db.StoreSettings.AnyAsync())
        {
            db.StoreSettings.Add(new StoreSettings
            {
                Id = StoreSettings.SingletonId,
                StoreAccountNumber = "0012345678",
                StoreAccountKey = "12",
                CreatedAtUtc = now,
                CreatedByUserId = ownerId
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private sealed record StockEvent(DateTime Date, Product Product, int Qty, StockMovementType Type,
        string Operator, string? Observation, Purchase? Purchase, InstalmentPlan? Plan, int CreatedBy);
}
