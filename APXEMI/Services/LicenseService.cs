using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace APXEMI.Services;

/// <summary>
/// Licence machine-bournée, hors-ligne : une empreinte matérielle stable
/// (MachineGuid + BIOS) identifie le poste, et un code d'activation signé
/// ECDSA P-256 (clé publique embarquée, clé privée chez l'éditeur) déverrouille
/// l'application. La licence validée est conservée dans %LOCALAPPDATA%\APXEMI.
/// </summary>
public sealed class LicenseService
{
    private static readonly string LicenseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "APXEMI");

    private static readonly string LicensePath = Path.Combine(LicenseDir, "license.lic");

    private readonly string? _publicKey;

    public LicenseService(IConfiguration configuration)
    {
        _publicKey = configuration["License:PublicKey"];
    }

    /// <summary>Empreinte matérielle lisible (ex. A1B2-C3D4-E5F6-A7B8).</summary>
    public string MachineId => ComputeMachineId();

    /// <summary>Vrai si une clé publique est configurée (sinon l'activation est impossible).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_publicKey);

    /// <summary>Vrai si une licence valide pour CE poste est enregistrée.</summary>
    public bool IsActivated => TryRead(out _);

    /// <summary>Nom du licencié (boutique) si la licence est valide, sinon null.</summary>
    public string? Licensee => TryRead(out var licensee) ? licensee : null;

    /// <summary>Vérifie, enregistre puis retourne true si le code est valide pour ce poste.</summary>
    public bool TryActivate(string code, out string? error)
    {
        error = null;
        var trimmed = code?.Trim() ?? string.Empty;

        if (!IsConfigured)
        {
            error = "Licence non configurée : la clé publique n'est pas définie (appsettings.json → License:PublicKey).";
            return false;
        }

        if (!TryVerify(trimmed, out _))
        {
            error = "Code d'activation invalide ou ne correspondant pas à ce poste.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(LicenseDir);
            File.WriteAllText(LicensePath, trimmed);
            return true;
        }
        catch (Exception ex)
        {
            error = "Impossible d'enregistrer la licence : " + ex.Message;
            return false;
        }
    }

    private bool TryRead(out string? licensee)
    {
        licensee = null;
        if (!IsConfigured || !File.Exists(LicensePath))
            return false;

        try
        {
            var code = File.ReadAllText(LicensePath).Trim();
            return TryVerify(code, out licensee);
        }
        catch
        {
            return false;
        }
    }

    private bool TryVerify(string code, out string? licensee)
    {
        licensee = null;
        if (string.IsNullOrWhiteSpace(_publicKey))
            return false;

        try
        {
            var parts = code.Split('.');
            if (parts.Length != 2)
                return false;

            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKey!), out _);

            if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
                return false;

            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
            if (payload is null || string.IsNullOrWhiteSpace(payload.MachineId))
                return false;

            // Le code doit être émis pour CE poste.
            if (!string.Equals(payload.MachineId, MachineId, StringComparison.OrdinalIgnoreCase))
                return false;

            licensee = payload.Licensee;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Empreinte stable : MachineGuid + informations BIOS/baseboard (registre).</summary>
    public static string ComputeMachineId()
    {
        var raw = string.Join("|",
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Cryptography", "MachineGuid"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardVersion"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "SystemSKU"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVendor"),
            ReadReg(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSReleaseDate"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        // 8 octets → 4 groupes de 4 hexa : XXXX-XXXX-XXXX-XXXX.
        return string.Join("-", Enumerable.Range(0, 4)
            .Select(i => Convert.ToHexString(hash, i * 2, 2)));
    }

    private static string ReadReg(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private sealed class LicensePayload
    {
        public string MachineId { get; set; } = "";
        public string Licensee { get; set; } = "";
        public string Issued { get; set; } = "";
    }
}
