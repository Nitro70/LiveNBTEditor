using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LiveNBT.App.Services;
using LiveNBT.App.ViewModels;

namespace LiveNBT.App;

/// <summary>Mutable editing copy of a <see cref="Profile"/> (records don't two-way bind).</summary>
public sealed class ProfileEdit : ViewModelBase
{
    private string _name = "New profile";
    private string _host = "127.0.0.1";
    private string _port = "25599";
    private string _token = "";

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Host { get => _host; set => Set(ref _host, value); }
    public string Port { get => _port; set => Set(ref _port, value); }
    public string Token { get => _token; set => Set(ref _token, value); }

    public Profile ToProfile() =>
        new(Name.Trim().Length == 0 ? "Unnamed" : Name.Trim(), Host.Trim(), int.TryParse(Port, out int p) ? p : 25599, Token.Trim());
}

/// <summary>Profile CRUD + app-behavior options. Replaces the old edit-JSON-in-notepad flow.</summary>
public partial class ProfilesWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<ProfileEdit> _profiles;

    public ProfilesWindow(MainViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        _vm = vm;
        _settings = settings;
        _profiles = new ObservableCollection<ProfileEdit>(vm.Profiles.Select(p => new ProfileEdit
        {
            Name = p.Name, Host = p.Host, Port = p.Port.ToString(), Token = p.Token,
        }));
        ProfileList.ItemsSource = _profiles;
        ProfileList.SelectedItem = _profiles.FirstOrDefault();
        AutoConnectBox.IsChecked = settings.AutoConnect;
        AutoReconnectBox.IsChecked = settings.AutoReconnect;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => Form.DataContext = ProfileList.SelectedItem;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var p = new ProfileEdit();
        _profiles.Add(p);
        ProfileList.SelectedItem = p;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileEdit p)
        {
            int i = _profiles.IndexOf(p);
            _profiles.Remove(p);
            ProfileList.SelectedItem = _profiles.Count > 0 ? _profiles[Math.Min(i, _profiles.Count - 1)] : null;
        }
    }

    private static string GameConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "config", "livenbt.json");

    /// <summary>Fill the selected profile from the local game's own config — no copy-pasting tokens.</summary>
    private void OnDetect(object sender, RoutedEventArgs e)
    {
        try
        {
            string file = GameConfigPath;
            if (!File.Exists(file))
            {
                DetectStatus.Text = "No livenbt.json found — launch Minecraft (with the LiveNBT agent) once, then try again.";
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            int port = doc.RootElement.TryGetProperty("port", out var po) && po.TryGetInt32(out int pv) ? pv : 25599;
            if (string.IsNullOrWhiteSpace(token))
            {
                DetectStatus.Text = "livenbt.json exists but has no token — start the game once so it can generate one.";
                return;
            }
            if (ProfileList.SelectedItem is not ProfileEdit p) { OnAdd(sender, e); p = (ProfileEdit)ProfileList.SelectedItem!; }
            p.Host = "127.0.0.1";
            p.Port = port.ToString();
            p.Token = token!;
            DetectStatus.Text = "Found it — host, port and token filled in from this PC's game config.";
        }
        catch (Exception ex)
        {
            DetectStatus.Text = "Couldn't read the game config: " + ex.Message;
        }
    }

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(GameConfigPath)!;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch (Exception ex) { DetectStatus.Text = "Couldn't open the folder: " + ex.Message; }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // a typo'd port used to silently save as 25599 — surface it instead
        foreach (var p in _profiles)
        {
            if (!int.TryParse(p.Port, out int port) || port is < 1 or > 65535)
            {
                ProfileList.SelectedItem = p;
                DetectStatus.Text = $"Profile \"{p.Name}\": the port must be a number from 1 to 65535.";
                return;
            }
        }
        _vm.Profiles.Clear();
        foreach (var p in _profiles) _vm.Profiles.Add(p.ToProfile());
        _vm.SaveProfiles();
        _settings.AutoConnect = AutoConnectBox.IsChecked == true;
        _settings.AutoReconnect = AutoReconnectBox.IsChecked == true;
        _vm.AutoReconnect = _settings.AutoReconnect;
        DialogResult = true;
    }
}
