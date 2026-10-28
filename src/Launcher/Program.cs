using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Windows.Media.Effects;

namespace SwgOldFlurryLauncher;

public static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [STAThread] public static void Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID("OldFlurry.Launcher");
        bool distribution = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(x=>x.Key=="Distribution" && x.Value=="SelfContained");
        string installedLauncher=Path.Combine(UpdateService.Data,"Launcher","OldFlurryLauncher.exe");
        if (distribution && !Path.GetFullPath(Environment.ProcessPath!).Equals(installedLauncher,StringComparison.OrdinalIgnoreCase))
        {
            try { InstallLauncher(args); }
            catch (Exception e) { MessageBox.Show(e.Message, "Launcher setup failed"); }
            return;
        }
        using var mutex = new Mutex(true, "Local\\OldFlurryLauncherV2", out bool created);
        if (!created) { MessageBox.Show("Old Flurry Launcher is already open."); return; }
        if(args.Contains("--rollback"))
        {
            try {MessageBox.Show(new RollbackService().Restore(ManagedClient.Location(),new Progress<Transfer>()).GetAwaiter().GetResult(),"Old Flurry recovery");}
            catch(Exception e){MessageBox.Show(e.Message,"Old Flurry recovery");}
        }
        new Application().Run(new LauncherWindow());
    }
    public static void InstallLauncher(string[] args)
    {
        var folder = Path.Combine(UpdateService.Data, "Launcher"); Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "OldFlurryLauncher.exe");
        if (!Path.GetFullPath(Environment.ProcessPath!).Equals(exe, StringComparison.OrdinalIgnoreCase))
        {
            try {File.Copy(Environment.ProcessPath!,exe,true);}
            catch(IOException e){throw new IOException("Close the installed Old Flurry launcher, then run this download again.",e);}
        }
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Old Flurry Launcher.lnk");
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Old Flurry Launcher.lnk");
        var script = "$w=New-Object -ComObject WScript.Shell; foreach($p in @(" + Q(desktop) + "," + Q(menu) + ")) { $s=$w.CreateShortcut($p); $s.TargetPath=" + Q(exe) + "; $s.WorkingDirectory=" + Q(folder) + "; $s.IconLocation=" + Q(exe + ",0") + "; $s.Save() }";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(info)!; process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create launcher shortcuts.");
        var launch=new ProcessStartInfo(exe){UseShellExecute=true};foreach(string arg in args)launch.ArgumentList.Add(arg);Process.Start(launch);
    }
}

public sealed class LauncherWindow : Window
{
    readonly UpdateService updates = new();
    readonly ServerStatusService serverStatus=new();
    readonly TextBlock serverText=new(){Text="SERVER: CHECKING…",Foreground=Brushes.LightGray,FontSize=14,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(24),Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=8,ShadowDepth=2,Opacity=1}};
    readonly TextBlock status = new() { Text = "Click Download & Install to get started.", TextWrapping = TextWrapping.Wrap, FontSize = 18 };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 12, Foreground = Brushes.OrangeRed, Background = new SolidColorBrush(Color.FromRgb(38,39,45)), BorderBrush = Brushes.DimGray };
    readonly Button play = MakeButton("DOWNLOAD & INSTALL", true);
    string client = "";
    string operationLabel="CHECKING…";
    bool busy, ready;
    readonly System.Windows.Threading.DispatcherTimer checkTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    readonly System.Windows.Threading.DispatcherTimer serverTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly System.Windows.Threading.DispatcherTimer usernameTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool readingServer;
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
        ShowInTaskbar=true;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/OldFlurryLauncher;component/app.ico"));
        var root = new Grid();
        root.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/OldFlurryLauncher;component/background.png")), Stretch = Stretch.UniformToFill });
        root.Children.Add(serverText);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(36), Background = Brushes.Transparent };
        var inner = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(inner);
        inner.Children.Add(new TextBlock { Text = "SWG OLD FLURRY", FontSize = 34, FontWeight = FontWeights.Bold });
        inner.Children.Add(new TextBlock { Text = "DARK PATH EDITION  •  OLD FLURRY CLIENT", Foreground = Brushes.OrangeRed, Margin = new Thickness(0, 0, 0, 18) });
        inner.Children.Add(status);
        bar.Margin = new Thickness(0, 16, 0, 8); inner.Children.Add(bar); inner.Children.Add(detail);
        play.FontSize=18;play.MinWidth=225;
        var primaryRow=new StackPanel {Margin=new Thickness(0,14,0,0),HorizontalAlignment=HorizontalAlignment.Left};primaryRow.Children.Add(play);inner.Children.Add(primaryRow);
        foreach(var label in inner.Children.OfType<TextBlock>())label.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=8,ShadowDepth=2,Opacity=1};
        root.Children.Add(panel); Content = root;
        play.Click += async (_, _) => await Run(async () =>
        {
            if(client.Length==0)
            {
                string destination=ManagedClient.Location();
                status.Text="Downloading and installing Old Flurry…";
                await updates.InstallBase(destination,Progress());client=destination;await Check(true);return;
            }
            bool launch=ready;
            // Always obtain the live manifest immediately before launching; a disabled button alone is insufficient.
            await Check(true);
            if (!ready || !launch) return;
            FlurryLoginConfigurator.Configure(client);
            status.Text="Starting Old Flurry…";operationLabel="STARTING…";Buttons();
            var launched=await GameLaunchService.Start(client);
            status.Text=launched.WindowOpened?"Old Flurry game window opened.":"Old Flurry is still starting.";
            detail.Text=launched.WindowOpened?"Game process and window confirmed running.":"The game process is running, but a game window has not appeared yet.";
        });
        Loaded += async (_, _) => await Run(async () =>
        {
            if (ManagedClient.Installed())client=ManagedClient.Location();
            if (client.Length > 0) await Check(true);
        });
        Loaded+=async(_,_)=>await RefreshServer();
        serverTimer.Tick+=async(_,_)=>await RefreshServer();serverTimer.Start();
        usernameTimer.Tick+=(_,_)=>{if(!busy&&client.Length>0){try{UsernameMemory.RememberMostRecent(client);}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){}}};usernameTimer.Start();
        checkTimer.Tick += async (_, _) => { if (!busy && client.Length > 0) await Run(() => Check(true)); };
        checkTimer.Start();
        Closing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show("Please wait for the current operation to finish."); } };
        Closed += (_, _) => { checkTimer.Stop(); serverTimer.Stop(); usernameTimer.Stop(); updates.Dispose(); serverStatus.Dispose(); };
        Buttons();
    }
    async Task RefreshServer()
    {
        if(readingServer)return;readingServer=true;
        try
        {
            string value=await serverStatus.Read();serverText.Text="SERVER: "+value;
            serverText.Foreground=value=="ONLINE"?Brushes.LightGreen:value=="OFFLINE"?Brushes.OrangeRed:Brushes.LightGray;
        }
        finally{readingServer=false;}
    }
    IProgress<Transfer> Progress() => new Progress<Transfer>(p =>
    {
        if (progressTimer.ElapsedMilliseconds < 75 && p.Done != p.Total && p.Done != 0) return;
        progressTimer.Restart(); bar.Value = p.Percent;
        detail.Text = p.Total >= 1_000_000 ? $"{p.Stage}: {p.Percent:F1}% — {p.Done / 1048576.0:F1} / {p.Total / 1048576.0:F1} MiB" : $"{p.Stage}: {p.Done:N0} / {p.Total:N0}";
    });
    void Buttons()
    {
        play.Content=busy?operationLabel:client.Length==0?"DOWNLOAD & INSTALL":ready?"PLAY":"UPDATE";
        play.IsEnabled = !busy;
    }
    async Task Run(Func<Task> operation)
    {
        if (busy) return; busy = true; operationLabel=client.Length==0?"INSTALLING…":"CHECKING…"; Buttons();
        try { await operation(); }
        catch (Exception e) { ready = false; status.Text = "Action required — retry when ready."; detail.Text = e.Message; bar.Value = 0; }
        finally { busy = false; Buttons(); }
    }
    async Task Check(bool apply)
    {
        ready = false; status.Text = "Checking required updates…"; bar.Value = 0;
        RuntimeConfiguration.Repair(client);
        UsernameMemory.RememberMostRecent(client);
        FlurryLoginConfigurator.Configure(client);
        var p = await updates.LatestAssets();
        if (!await updates.Current(client, p, Progress()))
        {
            status.Text = "Required update: Old Flurry " + p.Version;
            if (!apply) { detail.Text = "Click UPDATE to continue. Play unlocks after verification."; return; }
            operationLabel="UPDATING…";Buttons();
            await updates.InstallAssets(client, p, Progress());
            if (!await updates.Current(client, p, Progress())) throw new IOException("Installed assets did not pass verification.");
            var latest = await updates.LatestAssets();
            if (latest.Version != p.Version || !latest.Sha256.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "A newer required update is now available.";
                detail.Text = "Click UPDATE to install " + latest.Version + ".";
                return;
            }
        }
        ready = true; bar.Value = 100; status.Text = "Ready to play — Old Flurry " + p.Version;
        detail.Text = "Required assets verified. Updates use the existing public TRE feed.";
    }
}
