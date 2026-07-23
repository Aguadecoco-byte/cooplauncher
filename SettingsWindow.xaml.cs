using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace RemotePlayLauncher;

public partial class SettingsWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private readonly List<SteamGame> _allGames;
    private readonly LauncherConfig _config;
    private SteamGame? _selected;

    public SettingsWindow(List<SteamGame> games, LauncherConfig config)
    {
        InitializeComponent();
        _allGames = games;
        _config = config;

        if (!string.IsNullOrWhiteSpace(config.DonorExePath) && File.Exists(config.DonorExePath))
        {
            CurrentDonorName.Text = config.DonorInstallation?.GameName
                                    ?? Path.GetFileNameWithoutExtension(config.DonorExePath);
            CurrentDonorPath.Text = config.DonorExePath;
        }

        RestoreBtn.IsEnabled = !string.IsNullOrWhiteSpace(config.DonorExePath);
        RenderDonorList(_allGames);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        try
        {
            var background = 0x181112;
            DwmSetWindowAttribute(hwnd, 35, ref background, sizeof(int));
        }
        catch { }
    }

    private void RenderDonorList(IEnumerable<SteamGame> games)
    {
        DonorList.ItemsSource = games.OrderBy(game => game.Name).ToList();
    }

    private void DonorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = DonorList.SelectedItem as SteamGame;
        InstallBtn.IsEnabled = _selected != null;
        ActionStatus.Text = _selected == null ? string.Empty : $"Se instalará en: {_selected.GameFolder}";
    }

    private void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        try
        {
            if (DonorInstallationService.IsRunningFromConfiguredDonor(_config))
            {
                MessageBox.Show(
                    "Para reparar o cambiar el donante sin dejar archivos bloqueados, cierra esta ventana y abre Coop Launcher desde el acceso directo del escritorio.",
                    "Usa el acceso directo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var state = DonorInstallationService.Install(_selected, _config);
            CurrentDonorName.Text = _selected.Name;
            CurrentDonorPath.Text = state.TargetPath;
            RestoreBtn.IsEnabled = true;

            MessageBox.Show(
                $"Instalación verificada.\n\nAhora inicia \"{_selected.Name}\" desde Steam para usar Remote Play Together.",
                "Coop Launcher listo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Write("Donor installation failed.", ex);
            ActionStatus.Text = $"⚠  {ex.Message}";
        }
    }

    private void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DonorInstallationService.IsRunningFromConfiguredDonor(_config))
            {
                MessageBox.Show(
                    "Cierra el donante y abre Coop Launcher desde el acceso directo del escritorio antes de restaurarlo.",
                    "El donante está en uso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DonorInstallationService.Restore(_config);
            CurrentDonorName.Text = "Ninguno";
            CurrentDonorPath.Text = "Selecciona un juego arriba...";
            RestoreBtn.IsEnabled = false;
            ActionStatus.Text = "El ejecutable original fue restaurado y verificado.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Donor restore failed.", ex);
            ActionStatus.Text = $"⚠  {ex.Message}";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint != null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allGames
            : _allGames.Where(game => game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        RenderDonorList(filtered);
        _selected = null;
        InstallBtn.IsEnabled = false;
    }
}
