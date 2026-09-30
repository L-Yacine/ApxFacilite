namespace APXEMI.ViewModels;

public class LicenseViewModel
{
    public string MachineId { get; set; } = "";

    public bool IsConfigured { get; set; }

    public bool IsActivated { get; set; }

    public string? Licensee { get; set; }
}
