using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RemotePlayLauncher;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private static readonly HttpClient ImageHttp = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly DispatcherTimer _overlayPump;
    private readonly bool _isDonorSession;
    private LauncherConfig _config;
    private List<SteamGame> _steamGames = [];
    private List<LibraryEntry> _libraryEntries = [];
    private int _loadGeneration;
    private bool _windowRestartInProgress;

    public MainWindow()
    {
        _config = LauncherConfig.Load();
        _isDonorSession = DonorInstallationService.IsRunningFromConfiguredDonor(_config);
        InitializeComponent();

        if (_isDonorSession && _config.MaximizeInDonorMode)
            WindowState = WindowState.Maximized;

        _overlayPump = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1000d / 60d)
        };
        _overlayPump.Tick += OverlayPump_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        UpdateDonorStatus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        try
        {
            var background = 0x171717;
            DwmSetWindowAttribute(hwnd, 35, ref background, sizeof(int));
            var foreground = 0xF5F5F5;
            DwmSetWindowAttribute(hwnd, 36, ref foreground, sizeof(int));
        }
        catch { }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isDonorSession)
        {
            CloseStandaloneLauncherWindow();
            StartOverlayCompatibilityPump();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        AppLog.Write($"Main window loaded. Donor={_isDonorSession}; DPI={dpi.PixelsPerInchX:0}x{dpi.PixelsPerInchY:0}; size={ActualWidth:0}x{ActualHeight:0} DIPs.");
        await LoadGamesAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _overlayPump.Stop();
        _loadGeneration++;
    }

    private void OverlayPump_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;

        // OverlayFrame spans the entire client and renders an opaque hardware
        // background. Invalidating it makes WPF present a full-size dirty rect,
        // preventing Steam from treating a small list-row update as the viewport.
        OverlayFrame.InvalidateVisual();
        RootSurface.InvalidateVisual();
    }

    private void StartOverlayCompatibilityPump()
    {
        if (!_config.OverlayCompatibilityMode || _overlayPump.IsEnabled) return;
        _overlayPump.Start();
        AppLog.Write("Full-frame Steam Overlay compatibility pump started at 60 FPS.");
    }

    private void CloseStandaloneLauncherWindow()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("CoopLauncher"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName,
                            DonorInstallationService.CanonicalLauncherPath,
                            StringComparison.OrdinalIgnoreCase))
                            process.CloseMainWindow();
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex) { AppLog.Write("Could not close a standalone launcher window.", ex); }
    }

    private bool IsDonorGame(SteamGame game)
    {
        if (string.IsNullOrWhiteSpace(_config.DonorExePath)) return false;
        if (string.Equals(game.ExecutablePath, _config.DonorExePath, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var donor = Path.GetFullPath(_config.DonorExePath);
            var folder = Path.GetFullPath(game.GameFolder)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return donor.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void UpdateDonorStatus()
    {
        if (string.IsNullOrWhiteSpace(_config.DonorExePath))
        {
            DonorRun.Text = "No hay donante configurado — abre ⚙ Donante para seleccionar uno.";
            OverlayBtn.Content = "🎮  Iniciar Steam";
            return;
        }

        var mode = _isDonorSession ? "Remote Play activo" : "modo configuración";
        var health = DonorInstallationService.IsHealthy(_config, out var detail)
            ? detail
            : (_config.DonorInstallation == null ? "instalación anterior" : detail);
        DonorRun.Text = $"{mode} · Donante: {_config.DonorInstallation?.GameName ?? Path.GetFileNameWithoutExtension(_config.DonorExePath)} · {health}";
        OverlayBtn.Content = _isDonorSession ? "🎮  Controles" : "🎮  Iniciar Steam";
    }

    private async Task LoadGamesAsync()
    {
        var generation = ++_loadGeneration;
        LoadingText.Visibility = Visibility.Visible;
        NoResultsText.Visibility = Visibility.Collapsed;
        SubtitleText.Text = "Escaneando la biblioteca de Steam…";

        try
        {
            var games = await Task.Run(SteamDiscovery.Discover);
            if (generation != _loadGeneration) return;
            _steamGames = games;
            BuildLibraryEntries();

            LoadingText.Visibility = Visibility.Collapsed;
            SubtitleText.Text = $"{_steamGames.Count(game => !IsDonorGame(game))} de Steam · {_config.CustomGames.Count} externos";
            RenderFilteredEntries();

            if (string.IsNullOrWhiteSpace(_config.DonorExePath))
            {
                var choice = MessageBox.Show(
                    "Para usar Remote Play Together debes configurar un juego donante de Steam. ¿Quieres hacerlo ahora?",
                    "Configurar donante",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                    OpenDonorSettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Steam library scan failed.", ex);
            LoadingText.Visibility = Visibility.Collapsed;
            SubtitleText.Text = "No se pudo escanear Steam";
            DonorRun.Text = $"⚠  {ex.Message}";
        }
    }

    private void BuildLibraryEntries()
    {
        var entries = new List<LibraryEntry>();
        foreach (var game in _steamGames.Where(game => !IsDonorGame(game)))
        {
            _config.ExecutableOverrides.TryGetValue(game.AppId, out var executableOverride);
            var executable = !string.IsNullOrWhiteSpace(executableOverride) && File.Exists(executableOverride)
                ? executableOverride
                : game.ExecutablePath;
            entries.Add(new LibraryEntry
            {
                Id = "steam:" + game.AppId,
                Name = game.Name,
                DisplayPath = string.IsNullOrWhiteSpace(executable) ? game.GameFolder : executable,
                ExecutablePath = executable,
                WorkingDirectory = game.GameFolder,
                SteamGame = game
            });
        }

        foreach (var profile in _config.CustomGames)
        {
            entries.Add(new LibraryEntry
            {
                Id = "custom:" + profile.Id,
                Name = profile.Name,
                DisplayPath = profile.SourcePath,
                ExecutablePath = profile.ExecutablePath,
                Arguments = profile.Arguments,
                WorkingDirectory = profile.WorkingDirectory,
                RunAsAdministrator = profile.RunAsAdministrator,
                CustomProfile = profile
            });
        }

        _libraryEntries = entries
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void RenderFilteredEntries()
    {
        var query = SearchBox.Text.Trim();
        var entries = string.IsNullOrWhiteSpace(query)
            ? _libraryEntries
            : _libraryEntries.Where(entry =>
                entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || entry.DisplayPath.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        RenderList(entries);
    }

    private void RenderList(IEnumerable<LibraryEntry> entries)
    {
        foreach (var button in GameList.Children.OfType<Button>().ToList())
            GameList.Children.Remove(button);

        var list = entries.ToList();
        NoResultsText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in list)
            GameList.Children.Add(MakeGameRow(entry));
    }

    private Button MakeGameRow(LibraryEntry entry)
    {
        var letter = new TextBlock
        {
            Text = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 104, 150)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var imageBrush = new ImageBrush { Stretch = entry.IsSteam ? Stretch.UniformToFill : Stretch.Uniform };
        var image = new System.Windows.Shapes.Rectangle
        {
            Width = 130,
            Height = 61,
            RadiusX = 7,
            RadiusY = 7,
            Fill = imageBrush,
            Visibility = Visibility.Collapsed
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var imageGrid = new Grid { Width = 130, Height = 61 };
        imageGrid.Children.Add(letter);
        imageGrid.Children.Add(image);
        var imageBorder = new Border
        {
            Width = 130,
            Height = 61,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(26, 24, 38)),
            Child = imageGrid
        };

        _ = LoadEntryIconAsync(entry, imageBrush, image, letter, imageBorder);

        var name = new TextBlock
        {
            Text = entry.Name,
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var path = new TextBlock
        {
            Text = entry.DisplayPath,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(144, 140, 160)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0)
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        text.Children.Add(name);
        text.Children.Add(path);

        var badgeText = entry.IsSteam
            ? "STEAM"
            : (entry.RunAsAdministrator ? "EXTERNO · ADMIN" : "EXTERNO");
        var badge = new TextBlock
        {
            Text = badgeText,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = entry.RunAsAdministrator
                ? new SolidColorBrush(Color.FromRgb(245, 194, 92))
                : new SolidColorBrush(Color.FromRgb(144, 140, 160)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 12, 0)
        };
        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 96)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(imageBorder, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(badge, 2);
        Grid.SetColumn(arrow, 3);
        row.Children.Add(imageBorder);
        row.Children.Add(text);
        row.Children.Add(badge);
        row.Children.Add(arrow);

        var button = new Button { Style = (Style)FindResource("GameRow"), Content = row };
        button.Click += (_, _) => LaunchEntry(entry);
        button.ContextMenu = BuildContextMenu(entry);
        return button;
    }

    private ContextMenu BuildContextMenu(LibraryEntry entry)
    {
        var menu = new ContextMenu();
        if (entry.SteamGame != null)
        {
            var changeExecutable = new MenuItem { Header = "Cambiar ejecutable…" };
            changeExecutable.Click += (_, _) => PickExecutableFor(entry.SteamGame);
            menu.Items.Add(changeExecutable);
        }
        else if (entry.CustomProfile != null)
        {
            var admin = new MenuItem
            {
                Header = "Ejecutar como administrador",
                IsCheckable = true,
                IsChecked = entry.CustomProfile.RunAsAdministrator
            };
            admin.Click += (_, _) => ToggleAdministrator(entry.CustomProfile, admin.IsChecked);
            menu.Items.Add(admin);

            var replace = new MenuItem { Header = "Cambiar acceso directo o ejecutable…" };
            replace.Click += (_, _) => ReplaceCustomProfile(entry.CustomProfile);
            menu.Items.Add(replace);

            var remove = new MenuItem { Header = "Quitar de la lista" };
            remove.Click += (_, _) => RemoveCustomProfile(entry.CustomProfile);
            menu.Items.Add(remove);
        }

        var openFolder = new MenuItem { Header = "Abrir ubicación" };
        openFolder.Click += (_, _) => OpenEntryFolder(entry);
        menu.Items.Add(openFolder);
        return menu;
    }

    private async Task LoadEntryIconAsync(
        LibraryEntry entry,
        ImageBrush brush,
        FrameworkElement visual,
        FrameworkElement letter,
        Border border)
    {
        try
        {
            ImageSource? image = entry.SteamGame != null
                ? await LoadSteamImageAsync(entry.SteamGame.AppId)
                : LoadCustomImage(entry.CustomProfile!);
            if (image == null) return;

            brush.ImageSource = image;
            visual.Visibility = Visibility.Visible;
            letter.Visibility = Visibility.Collapsed;
            border.Background = Brushes.Transparent;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Artwork failed for {entry.Name}.", ex);
        }
    }

    private static async Task<ImageSource?> LoadSteamImageAsync(string appId)
    {
        var localPath = SteamDiscovery.GetLocalIconPath(appId);
        if (localPath != null)
            return await DecodeBytesAsync(await File.ReadAllBytesAsync(localPath));

        var gridUrl = await SteamGridDbService.GetGridUrlAsync(appId);
        if (!string.IsNullOrWhiteSpace(gridUrl))
        {
            var grid = await DownloadAndDecodeAsync(gridUrl);
            if (grid != null) return grid;
        }

        return await DownloadAndDecodeAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg");
    }

    private static ImageSource? LoadCustomImage(CustomLaunchProfile profile)
    {
        var iconPath = File.Exists(profile.IconPath) ? profile.IconPath
                     : File.Exists(profile.ExecutablePath) ? profile.ExecutablePath
                     : profile.SourcePath;
        if (!File.Exists(iconPath)) return null;

        var extension = Path.GetExtension(iconPath).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico")
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 240;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
        if (icon == null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(128, 128));
        source.Freeze();
        return source;
    }

    private static async Task<BitmapImage?> DownloadAndDecodeAsync(string url)
    {
        try { return await DecodeBytesAsync(await ImageHttp.GetByteArrayAsync(url)); }
        catch { return null; }
    }

    private static Task<BitmapImage?> DecodeBytesAsync(byte[] bytes) => Task.Run<BitmapImage?>(() =>
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.DecodePixelWidth = 240;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    });

    private void LaunchEntry(LibraryEntry entry)
    {
        try
        {
            if (entry.CustomProfile != null)
            {
                if (entry.CustomProfile.RunAsAdministrator)
                {
                    var answer = MessageBox.Show(
                        "Windows mostrará UAC. Las aplicaciones elevadas pueden bloquear el mando, teclado o ratón del invitado de Remote Play.\n\n¿Continuar como administrador?",
                        "Ejecutar como administrador",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes) return;
                }
                LaunchService.Start(entry.CustomProfile);
            }
            else
            {
                var executable = entry.ExecutablePath;
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    executable = PickExecutableFor(entry.SteamGame!);
                    if (string.IsNullOrWhiteSpace(executable)) return;
                }

                Process.Start(new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Directory.Exists(entry.WorkingDirectory)
                        ? entry.WorkingDirectory
                        : Path.GetDirectoryName(executable) ?? string.Empty,
                    UseShellExecute = true
                });
            }

            DonorRun.Text = $"Iniciando {entry.Name}… El host permanece activo para Remote Play.";
            StartOverlayCompatibilityPump();
        }
        catch (OperationCanceledException)
        {
            DonorRun.Text = "La solicitud de administrador fue cancelada.";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Launch failed for {entry.Name}.", ex);
            DonorRun.Text = $"⚠  {ex.Message}";
        }
    }

    private string PickExecutableFor(SteamGame game)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Seleccionar ejecutable para {game.Name}",
            InitialDirectory = game.GameFolder,
            Filter = "Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true || !File.Exists(dialog.FileName))
            return string.Empty;
        try
        {
            _config.ExecutableOverrides[game.AppId] = dialog.FileName;
            _config.Save();
            BuildLibraryEntries();
            RenderFilteredEntries();
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            AppLog.Write("Executable override could not be saved.", ex);
            MessageBox.Show(ex.Message, "Coop Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return string.Empty;
        }
    }

    private void AddGameBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Añadir accesos directos o aplicaciones",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Filter = "Accesos y aplicaciones|*.lnk;*.exe;*.bat;*.cmd;*.com;*.url|Accesos directos (*.lnk)|*.lnk|Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var errors = new List<string>();
        var elevatedCount = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var imported = ShortcutResolver.Import(file);
                var existing = _config.CustomGames.FirstOrDefault(profile =>
                    string.Equals(profile.SourcePath, imported.SourcePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    imported.Id = existing.Id;
                    _config.CustomGames.Remove(existing);
                }
                _config.CustomGames.Add(imported);
                if (imported.RunAsAdministrator) elevatedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                AppLog.Write($"Import failed for {file}.", ex);
            }
        }

        try
        {
            _config.Save();
            BuildLibraryEntries();
            RenderFilteredEntries();
            SubtitleText.Text = $"{_steamGames.Count(game => !IsDonorGame(game))} de Steam · {_config.CustomGames.Count} externos";
        }
        catch (Exception ex)
        {
            errors.Add("No se pudo guardar la configuración: " + ex.Message);
            AppLog.Write("Custom profiles could not be saved.", ex);
        }

        if (elevatedCount > 0)
        {
            MessageBox.Show(
                "Los accesos directos se configuraron para ejecutarse como administrador, como solicitaste. Windows pedirá confirmación UAC cada vez.\n\nSi el segundo jugador no recibe controles, haz clic derecho en el juego y desactiva «Ejecutar como administrador».",
                "Accesos añadidos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        if (errors.Count > 0)
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Algunos archivos no se añadieron", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ToggleAdministrator(CustomLaunchProfile profile, bool enabled)
    {
        profile.RunAsAdministrator = enabled;
        SaveProfilesAndRefresh();
    }

    private void ReplaceCustomProfile(CustomLaunchProfile current)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Cambiar archivo de {current.Name}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Filter = "Accesos y aplicaciones|*.lnk;*.exe;*.bat;*.cmd;*.com;*.url|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var replacement = ShortcutResolver.Import(dialog.FileName);
            replacement.Id = current.Id;
            replacement.Name = current.Name;
            var index = _config.CustomGames.IndexOf(current);
            _config.CustomGames[index] = replacement;
            SaveProfilesAndRefresh();
        }
        catch (Exception ex)
        {
            AppLog.Write("Custom profile replacement failed.", ex);
            MessageBox.Show(ex.Message, "Coop Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveCustomProfile(CustomLaunchProfile profile)
    {
        if (MessageBox.Show(
                $"¿Quitar «{profile.Name}» de Coop Launcher? No se borrará ningún archivo.",
                "Quitar de la lista",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _config.CustomGames.Remove(profile);
        SaveProfilesAndRefresh();
    }

    private void SaveProfilesAndRefresh()
    {
        try
        {
            _config.Save();
            BuildLibraryEntries();
            RenderFilteredEntries();
        }
        catch (Exception ex)
        {
            AppLog.Write("Profile setting could not be saved.", ex);
            MessageBox.Show(ex.Message, "Coop Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OpenEntryFolder(LibraryEntry entry)
    {
        var candidate = entry.CustomProfile?.SourcePath ?? entry.ExecutablePath;
        var directory = Directory.Exists(candidate) ? candidate : Path.GetDirectoryName(candidate);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OpenWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshRunningWindowPicker();
        DesktopLimitationNotice.Visibility = Visibility.Collapsed;
        WindowRestartConsent.IsChecked = false;
        WindowPickerOverlay.Visibility = Visibility.Visible;
        WindowPickerStatus.Text = _isDonorSession
            ? "Selecciona la aplicación que quieres reiniciar dentro de la sesión."
            : "Abre primero Coop Launcher desde el juego donante de Steam; solo así Steam podrá registrar la aplicación.";
        RestartWindowBtn.IsEnabled = _isDonorSession;
        AppLog.Write("The in-window application picker was opened without creating a second HWND.");
    }

    private void RefreshRunningWindowPicker()
    {
        var selectedPid = (RunningWindowList.SelectedItem as RunningWindowInfo)?.ProcessId;
        var windows = WindowDiscoveryService.Discover();
        RunningWindowList.ItemsSource = windows;
        if (selectedPid.HasValue)
            RunningWindowList.SelectedItem = windows.FirstOrDefault(window => window.ProcessId == selectedPid.Value);
    }

    private void WindowPickerRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRestartInProgress) return;
        RefreshRunningWindowPicker();
        WindowPickerStatus.Text = $"{RunningWindowList.Items.Count} aplicaciones disponibles.";
    }

    private void WindowPickerCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRestartInProgress)
        {
            WindowPickerStatus.Text = "Espera a que termine el reinicio seguro de la aplicación.";
            return;
        }
        CloseRunningWindowPicker();
    }

    private void CloseRunningWindowPicker()
    {
        WindowPickerOverlay.Visibility = Visibility.Collapsed;
        RunningWindowList.SelectedItem = null;
        DesktopLimitationNotice.Visibility = Visibility.Collapsed;
        WindowRestartConsent.IsChecked = false;
    }

    private void RunningWindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RunningWindowList.SelectedItem is not RunningWindowInfo selected) return;
        WindowRestartConsent.IsChecked = false;
        WindowPickerStatus.Text = string.IsNullOrWhiteSpace(selected.ExecutablePath)
            ? "Windows no permite consultar el ejecutable de esta aplicación; no se puede incorporar de forma segura."
            : $"Seleccionada: {selected.Title}. Guarda tu trabajo antes de reiniciarla.";
    }

    private void DesktopExplanation_Click(object sender, RoutedEventArgs e)
    {
        DesktopLimitationNotice.Visibility = DesktopLimitationNotice.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void RestartWindowInsideSteam_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRestartInProgress) return;
        if (!_isDonorSession)
        {
            WindowPickerStatus.Text = "Esta acción solo funciona al abrir Coop Launcher mediante el juego donante de Steam.";
            return;
        }
        if (RunningWindowList.SelectedItem is not RunningWindowInfo selected)
        {
            WindowPickerStatus.Text = "Selecciona una aplicación primero.";
            return;
        }
        if (string.IsNullOrWhiteSpace(selected.ExecutablePath) || !File.Exists(selected.ExecutablePath))
        {
            WindowPickerStatus.Text = "No se pudo acceder al ejecutable de la aplicación seleccionada.";
            return;
        }
        if (WindowRestartConsent.IsChecked != true)
        {
            WindowPickerStatus.Text = "Marca la confirmación después de guardar tu trabajo.";
            return;
        }

        var unsupportedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "steam", "steamwebhelper", "ShellExperienceHost",
            "StartMenuExperienceHost", "SearchHost", "ApplicationFrameHost"
        };
        if (unsupportedProcesses.Contains(selected.ProcessName))
        {
            WindowPickerStatus.Text = "Esa aplicación forma parte del escritorio o de Steam y no puede reiniciarse como juego. Usa Steam Link para controlar el escritorio completo.";
            DesktopLimitationNotice.Visibility = Visibility.Visible;
            return;
        }

        _windowRestartInProgress = true;
        RestartWindowBtn.IsEnabled = false;
        RunningWindowList.IsEnabled = false;
        try
        {
            var profile = GetOrCreateTrackedProfile(selected);
            WindowPickerStatus.Text = $"Cerrando «{selected.Title}» de forma normal…";

            using var process = Process.GetProcessById(selected.ProcessId);
            if (!process.CloseMainWindow())
            {
                WindowPickerStatus.Text = "La aplicación no aceptó el cierre normal. Ciérrala manualmente y luego ábrela desde la lista de Coop Launcher.";
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Activate();
                WindowPickerStatus.Text = "La aplicación sigue abierta, posiblemente esperando que guardes cambios. Ciérrala por completo y después ábrela desde la lista principal.";
                return;
            }

            WindowPickerStatus.Text = $"Iniciando «{profile.Name}» dentro de Steam…";
            CloseRunningWindowPicker();
            var launched = LaunchService.StartTracked(profile);
            DonorRun.Text = $"{profile.Name} se inició dentro de Steam. El invitado recuperará imagen y controles cuando aparezca su ventana.";
            StartOverlayCompatibilityPump();
            AppLog.Write($"Restarted an existing application as a tracked donor child. SourcePid={selected.ProcessId}; NewPid={launched.Id}; Path={profile.ExecutablePath}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not restart window process {selected.ProcessId} inside Steam.", ex);
            WindowPickerOverlay.Visibility = Visibility.Visible;
            WindowPickerStatus.Text = "No se pudo reiniciar la aplicación: " + ex.Message;
        }
        finally
        {
            _windowRestartInProgress = false;
            RestartWindowBtn.IsEnabled = _isDonorSession;
            RunningWindowList.IsEnabled = true;
            BuildLibraryEntries();
            RenderFilteredEntries();
        }
    }

    private CustomLaunchProfile GetOrCreateTrackedProfile(RunningWindowInfo selected)
    {
        var existing = _config.CustomGames.FirstOrDefault(profile =>
            string.Equals(profile.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Unelevated launch is required so the donor can remain the direct
            // parent and Windows will not reject the guest's injected input.
            existing.RunAsAdministrator = false;
            _config.Save();
            return existing;
        }

        var profile = ShortcutResolver.Import(selected.ExecutablePath);
        profile.Name = string.IsNullOrWhiteSpace(selected.ProcessName)
            ? Path.GetFileNameWithoutExtension(selected.ExecutablePath)
            : selected.ProcessName;
        profile.RunAsAdministrator = false;
        _config.CustomGames.Add(profile);
        _config.Save();
        return profile;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || WindowPickerOverlay.Visibility != Visibility.Visible) return;
        WindowPickerCancel_Click(sender, e);
        e.Handled = true;
    }

    private void OverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isDonorSession)
        {
            WindowState = WindowState.Maximized;
            Show();
            Activate();
            StartOverlayCompatibilityPump();
            DonorRun.Text = "Pulsa Shift+Tab y abre el icono de controles/Remote Play para asignar al jugador 2.";
            return;
        }

        var appId = _config.DonorAppId
                    ?? _steamGames.FirstOrDefault(IsDonorGame)?.AppId;
        if (string.IsNullOrWhiteSpace(appId))
        {
            MessageBox.Show("Configura primero un juego donante.", "Coop Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            OpenDonorSettings();
            return;
        }

        Process.Start(new ProcessStartInfo($"steam://run/{appId}") { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_libraryEntries.Count > 0) RenderFilteredEntries();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        await LoadGamesAsync();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => OpenDonorSettings();

    private void OpenDonorSettings()
    {
        var window = new SettingsWindow(_steamGames, _config) { Owner = this };
        window.ShowDialog();
        _config = LauncherConfig.Load();
        UpdateDonorStatus();
        BuildLibraryEntries();
        RenderFilteredEntries();
    }

    private void CreditsBtn_Click(object sender, RoutedEventArgs e)
    {
        new CreditsWindow { Owner = this }.ShowDialog();
    }
}
