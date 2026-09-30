using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Outil RÉSERVÉ À L'ÉDITEUR — ne jamais distribuer avec l'application.
// Il détient la clé privée qui signe les codes d'activation.
//
//   LicenseGenerator gen                     → crée la paire de clés (une fois)
//   LicenseGenerator issue <idPoste> <boutique> → émet un code d'activation

var command = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

switch (command)
{
    case "gen":
        Gen();
        break;
    case "issue":
        Issue(args.Skip(1).ToArray());
        break;
    default:
        Console.WriteLine("Usage :");
        Console.WriteLine("  LicenseGenerator gen                          — crée la paire de clés (une fois)");
        Console.WriteLine("  LicenseGenerator issue <idPoste> <boutique>   — émet un code d'activation");
        break;
}

static void Gen()
{
    var keysDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "apxemi-keys");
    Directory.CreateDirectory(keysDir);
    var privatePath = Path.Combine(keysDir, "apxemi.private.key");

    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var privatePem = ecdsa.ExportPkcs8PrivateKeyPem();
    var publicBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

    File.WriteAllText(privatePath, privatePem);

    var appsettings = FindAppSettings();
    if (appsettings is not null)
    {
        var text = File.ReadAllText(appsettings);
        var updated = text.Replace("\"PublicKey\": \"\"", $"\"PublicKey\": \"{publicBase64}\"");
        if (!string.Equals(updated, text, StringComparison.Ordinal))
            File.WriteAllText(appsettings, updated);
    }

    Console.WriteLine($"Clé privée écrite : {privatePath}");
    Console.WriteLine($"Clé publique écrite dans : {(appsettings ?? "(introuvable — collez-la manuellement ci-dessous)")}");
    Console.WriteLine();
    Console.WriteLine("Clé publique (License:PublicKey) :");
    Console.WriteLine(publicBase64);
    Console.WriteLine();
    Console.WriteLine("IMPORTANT : gardez la clé privée en lieu sûr, ne la distribuez JAMAIS.");
}

static void Issue(string[] rest)
{
    if (rest.Length < 2)
    {
        Console.WriteLine("Usage : LicenseGenerator issue <idPoste> <boutique>");
        return;
    }

    var machineId = rest[0].Trim().ToUpperInvariant();
    var licensee = string.Join(" ", rest.Skip(1)).Trim();
    var issued = DateTime.Now.ToString("yyyy-MM-dd");

    var privatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "apxemi-keys", "apxemi.private.key");
    if (!File.Exists(privatePath))
    {
        Console.WriteLine($"Clé privée introuvable : {privatePath}");
        Console.WriteLine("Lancez d'abord : LicenseGenerator gen");
        return;
    }

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(privatePath));

    var payload = JsonSerializer.Serialize(new LicensePayload
    {
        MachineId = machineId,
        Licensee = licensee,
        Issued = issued
    });
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    var signature = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

    var code = Base64UrlEncode(payloadBytes) + "." + Base64UrlEncode(signature);

    Console.WriteLine("Code d'activation :");
    Console.WriteLine(code);
    Console.WriteLine();
    Console.WriteLine($"Poste : {machineId} — Boutique : {licensee} — Émis le : {issued}");
}

static string? FindAppSettings()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "APXEMI", "appsettings.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "APXEMI", "appsettings.json"),
    };
    foreach (var candidate in candidates)
    {
        var full = Path.GetFullPath(candidate);
        if (File.Exists(full))
            return full;
    }
    return null;
}

static string Base64UrlEncode(byte[] bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

internal sealed class LicensePayload
{
    public string MachineId { get; set; } = "";
    public string Licensee { get; set; } = "";
    public string Issued { get; set; } = "";
}
