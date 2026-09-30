using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Windows.Forms;

namespace APXEMI.Desktop;

public sealed class MainForm : Form
{
    private readonly WebView2 _webView;
    private readonly string _url;

    public MainForm(string url)
    {
        _url = url;
        Text = "APXEMI — Gestion EMI";
        Width = 1280;
        Height = 840;
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        Shown += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async(null);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Le composant WebView2 n'a pas pu démarrer.\n\n" +
                "Installez « WebView2 Runtime (Evergreen) » depuis Microsoft, puis relancez l'application.\n\n" +
                ex.Message,
                "APXEMI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }
}
