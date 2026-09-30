using Microsoft.AspNetCore.Builder;
using System.Windows.Forms;

namespace APXEMI.Desktop;

internal static class Launcher
{
    private const string AppUrl = "http://localhost:5147";

    [STAThread]
    private static void Main(string[] args)
    {
        // Une seule instance : un second lancement n'ouvre pas de fenêtre
        // ni de second serveur (qui échouerait sur le port déjà occupé).
        using var mutex = new Mutex(true, @"Local\APXEMI.Desktop.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "APXEMI est déjà en cours d'exécution.",
                "APXEMI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Le contenu racine (wwwroot, appsettings.json) doit être celui du
        // dossier de l'exécutable publié, pas le répertoire de travail courant.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        WebApplication app;
        try
        {
            app = global::Program.BuildApp(args);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible de démarrer l'application.\n\n" + ex.Message,
                "APXEMI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            global::Program.InitializeAsync(app).GetAwaiter().GetResult();
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible de démarrer le serveur local.\n\n" + ex.Message,
                "APXEMI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using var form = new MainForm(AppUrl);
        Application.Run(form);

        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
