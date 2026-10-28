using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SwgOldFlurryLauncher;

public static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [STAThread] public static void Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID("OldFlurry.Launcher");
        if (args.Contains("--install"))
        {
            try { InstallLauncher(); }
            catch (Exception e) { MessageBox.Show(e.Message, "Launcher setup failed"); }
            return;
        }
        using var mutex = new Mutex(true, "Local\\OldFlurryLauncherV2", out bool created);
        if (!created) { MessageBox.Show("Old Flurry Launcher is already open."); return; }
        new Application().Run(new LauncherWindow());
    }
    public static void InstallLauncher()
    {
        var folder = Path.Combine(UpdateService.Data, "Launcher"); Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "OldFlurryLauncher.exe");
        if (!Path.GetFullPath(Environment.ProcessPath!).Equals(exe, StringComparison.OrdinalIgnoreCase)) File.Copy(Environment.ProcessPath!, exe, true);
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Old Flurry Launcher.lnk");
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Old Flurry Launcher.lnk");
        var script = "$w=New-Object -ComObject WScript.Shell; foreach($p in @(" + Q(desktop) + "," + Q(menu) + ")) { $s=$w.CreateShortcut($p); $s.TargetPath=" + Q(exe) + "; $s.WorkingDirectory=" + Q(folder) + "; $s.IconLocation=" + Q(exe + ",0") + "; $s.Save() }";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(info)!; process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create launcher shortcuts.");
        MessageBox.Show("Launcher installed. Use the Desktop or Start Menu shortcut to play.\n\nTo keep it on your taskbar: open the launcher, right-click its taskbar icon, then choose Pin to taskbar.", "Old Flurry Launcher");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }
}

public sealed class LauncherWindow : Window
{
    readonly UpdateService updates = new();
    readonly TextBlock status = new() { Text = "Choose your client or install a fresh copy.", TextWrapping = TextWrapping.Wrap, FontSize = 18 };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    readonly TextBlock folderText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 12, Foreground = Brushes.OrangeRed };
    readonly Button play = MakeButton("PLAY", true);
    readonly Button update = MakeButton("CHECK / UPDATE");
    readonly Button browse = MakeButton("USE EXISTING CLIENT");
    readonly Button install = MakeButton("INSTALL NEW CLIENT");
    readonly Button shortcut = MakeButton("INSTALL LAUNCHER & SHORTCUTS");
    readonly string saved = Path.Combine(UpdateService.Data, "SelectedClient.txt");
    string client = "";
    bool busy, ready;
    readonly System.Windows.Threading.DispatcherTimer checkTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    readonly System.Diagnostics.Stopwatch progressTimer = System.Diagnostics.Stopwatch.StartNew();

    static Button MakeButton(string text, bool primary = false) => new()
    {
        Content = text, Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 6, 10, 6),
        Background = primary ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(38, 39, 45)), Foreground = Brushes.White,
        BorderBrush = Brushes.DimGray, FontWeight = FontWeights.SemiBold,
        Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}' CornerRadius='3'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")
    };
    public LauncherWindow()
    {
        Title = "SWG Old Flurry Launcher"; Width = 1020; Height = 690; MinWidth = 780; MinHeight = 620;
        Background = Brushes.Black; Foreground = Brushes.White;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/OldFlurryLauncher;component/app.ico"));
        var root = new Grid();
        root.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/OldFlurryLauncher;component/background.png")), Stretch = Stretch.UniformToFill, Opacity = .6 });
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(36), Background = new SolidColorBrush(Color.FromArgb(225, 12, 13, 18)) };
        var inner = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(inner);
        inner.Children.Add(new TextBlock { Text = "SWG OLD FLURRY", FontSize = 34, FontWeight = FontWeights.Bold });
        inner.Children.Add(new TextBlock { Text = "DARK PATH EDITION  •  LAUNCHER 2.0", Foreground = Brushes.OrangeRed, Margin = new Thickness(0, 0, 0, 18) });
        inner.Children.Add(status); inner.Children.Add(folderText);
        bar.Margin = new Thickness(0, 16, 0, 8); inner.Children.Add(bar); inner.Children.Add(detail);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; actions.Children.Add(play); actions.Children.Add(update); actions.Children.Add(browse); actions.Children.Add(install); actions.Children.Add(shortcut); inner.Children.Add(actions);
        root.Children.Add(panel); Content = root;
        browse.Click += async (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Select your SWG client folder" };
            if (dialog.ShowDialog(this) != true) return;
            if (!SwgClientLocator.IsValidClient(dialog.FolderName)) { MessageBox.Show("Choose the folder containing SwgClient_r.exe."); return; }
            await Run(async () => { Select(dialog.FolderName); await Check(false); });
        };
        update.Click += async (_, _) => await Run(() => Check(true));
        install.Click += async (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose an empty folder for your new Old Flurry client" };
            if (dialog.ShowDialog(this) != true) return;
            await Run(async () => { status.Text = "Installing a fresh Old Flurry client…"; await updates.InstallBase(dialog.FolderName, Progress()); Select(dialog.FolderName); await Check(true); });
        };
        shortcut.Click += (_, _) =>
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }; info.ArgumentList.Add("--install");
            Process.Start(info); Close();
        };
        play.Click += async (_, _) => await Run(async () =>
        {
            // Always obtain the live manifest immediately before launching; a disabled button alone is insufficient.
            await Check(true);
            if (!ready) return;
            FlurryLoginConfigurator.Configure(client);
            Process.Start(new ProcessStartInfo(Path.Combine(client, "SwgClient_r.exe")) { WorkingDirectory = client, UseShellExecute = true });
            status.Text = "Game launched. Keep this launcher for your next session.";
        });
        Loaded += async (_, _) => await Run(async () =>
        {
            string? previous = File.Exists(saved) ? (await File.ReadAllTextAsync(saved)).Trim() : null;
            var clients = SwgClientLocator.FindClients(previous);
            if (SwgClientLocator.IsValidClient(previous)) Select(previous!);
            else if (clients.Count == 1) Select(clients[0]);
            if (client.Length > 0) await Check(false);
        });
        checkTimer.Tick += async (_, _) => { if (!busy && client.Length > 0) await Run(() => Check(false)); };
        checkTimer.Start();
        Closing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show("Please wait for the current operation to finish."); } };
        Closed += (_, _) => { checkTimer.Stop(); updates.Dispose(); };
        Buttons();
    }
    IProgress<Transfer> Progress() => new Progress<Transfer>(p =>
    {
        if (progressTimer.ElapsedMilliseconds < 75 && p.Done != p.Total && p.Done != 0) return;
        progressTimer.Restart(); bar.Value = p.Percent;
        detail.Text = p.Total >= 1_000_000 ? $"{p.Stage}: {p.Percent:F1}% — {p.Done / 1048576.0:F1} / {p.Total / 1048576.0:F1} MiB" : $"{p.Stage}: {p.Done:N0} / {p.Total:N0}";
    });
    void Select(string folder)
    {
        client = Path.GetFullPath(folder); ready = false; folderText.Text = client;
        Directory.CreateDirectory(UpdateService.Data); File.WriteAllText(saved, client);
    }
    void Buttons()
    {
        play.IsEnabled = !busy && ready;
        update.IsEnabled = !busy && client.Length > 0;
        browse.IsEnabled = install.IsEnabled = shortcut.IsEnabled = !busy;
    }
    async Task Run(Func<Task> operation)
    {
        if (busy) return; busy = true; Buttons();
        try { await operation(); }
        catch (Exception e) { ready = false; status.Text = "Action required — retry when ready."; detail.Text = e.Message; bar.Value = 0; }
        finally { busy = false; Buttons(); }
    }
    async Task Check(bool apply)
    {
        ready = false; status.Text = "Checking required updates…"; bar.Value = 0;
        var p = await updates.LatestAssets();
        if (!await updates.Current(client, p, Progress()))
        {
            status.Text = "Required update: Old Flurry " + p.Version;
            if (!apply) { detail.Text = "Click CHECK / UPDATE to continue. Play unlocks after verification."; return; }
            await updates.InstallAssets(client, p, Progress());
            if (!await updates.Current(client, p, Progress())) throw new IOException("Installed assets did not pass verification.");
            var latest = await updates.LatestAssets();
            if (latest.Version != p.Version || !latest.Sha256.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "A newer required update is now available.";
                detail.Text = "Click CHECK / UPDATE to install " + latest.Version + ".";
                return;
            }
        }
        ready = true; bar.Value = 100; status.Text = "Ready to play — Old Flurry " + p.Version;
        detail.Text = "Required assets verified. Updates use the existing public TRE feed.";
    }
}
