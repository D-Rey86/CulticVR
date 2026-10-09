// Copyright (C) 2026 D-Rey86 and contributors. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("CULTIC VR Installer")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (C) 2026 D-Rey86 and contributors")]

internal static class Installer
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Same engine used by the UI; permits isolated acceptance tests without a window.
        if (args.Length == 3 && args[0] == "--run")
        {
            try { Console.WriteLine(Run(args[1], args[2])); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallWindow());
        return 0;
    }

    internal static IEnumerable<string> LibraryPaths(string steamRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        paths.Add(steamRoot);
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                paths.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }
        return paths;
    }

    internal static string FindGame()
    {
        var roots = new List<string>();
        foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
        {
            try
            {
                object value = Registry.GetValue(key, "SteamPath", null) ?? Registry.GetValue(key, "InstallPath", null);
                if (value is string) roots.Add((string)value);
            }
            catch { /* The folder picker remains available when registry access is denied. */ }
        }
        foreach (string root in roots)
        {
            try
            {
                foreach (string library in LibraryPaths(root))
                {
                    string candidate = Path.Combine(library, "steamapps", "common", "CULTIC");
                    if (File.Exists(Path.Combine(candidate, "CULTIC.exe"))) return candidate;
                }
            }
            catch { /* A disconnected Steam drive must not prevent browsing another. */ }
        }
        return "";
    }

    internal static string Quote(string text)
    {
        // Windows CommandLineToArgvW quoting, including trailing backslashes.
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in text)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }

    internal static string Run(string action, string game)
    {
        if (action != "Install" && action != "Remove") throw new ArgumentException("Unknown installer action.");
        game = Path.GetFullPath(game.Trim().Trim('"')).TrimEnd('\\');
        if (!File.Exists(Path.Combine(game, "CULTIC.exe"))) throw new IOException("Select the folder containing CULTIC.exe.");
        if (action == "Install" && File.Exists(Path.Combine(game, "CulticVR-install.json"))) action = "Update";
        string payload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload");
        string script = Path.Combine(payload, "Manage-CulticVR.ps1");
        if (!File.Exists(script)) throw new IOException("Extract the whole ZIP before running Install CulticVR.exe. The payload folder is missing.");
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script) + " -Action " + action + " -GameDir " + Quote(game),
            WorkingDirectory = payload,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // A launch from PowerShell 7 can carry incompatible module search paths.
        // Let Windows PowerShell initialize its own standard module locations.
        start.EnvironmentVariables.Remove("PSModulePath");
        using (var process = Process.Start(start))
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string details = output.Result + Environment.NewLine + errors.Result;
            if (process.ExitCode != 0) throw new IOException(details.Trim());
            return details.Trim();
        }
    }
}

internal sealed class InstallWindow : Form
{
    private readonly TextBox path = new TextBox { Dock = DockStyle.Fill };
    private readonly Button browse = new Button { Text = "Browse...", AutoSize = true };
    private readonly Button install = new Button { Text = "Install / Update", AutoSize = true };
    private readonly Button remove = new Button { Text = "Uninstall", AutoSize = true };
    private readonly TextBox status = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private bool busy;

    internal InstallWindow()
    {
        Text = "CULTIC VR — Chapter One";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(680, 360);
        MinimumSize = new Size(580, 350);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "CULTIC VR 1.0.0", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { Text = "Close CULTIC, select its game folder, then click Install / Update.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        var folder = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folder.Controls.Add(path, 0, 0); folder.Controls.Add(browse, 1, 0);
        layout.Controls.Add(folder);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        buttons.Controls.Add(install); buttons.Controls.Add(remove);
        layout.Controls.Add(buttons); layout.Controls.Add(status);
        Controls.Add(layout);
        path.Text = Installer.FindGame();
        status.Text = path.Text.Length > 0 ? "CULTIC found. All required mod components are included." : "Click Browse and select CULTIC.exe. All required mod components are included.";
        browse.Click += delegate
        {
            using (var picker = new OpenFileDialog { Title = "Select CULTIC.exe", Filter = "CULTIC|CULTIC.exe", CheckFileExists = true })
                if (picker.ShowDialog(this) == DialogResult.OK) path.Text = Path.GetDirectoryName(picker.FileName);
        };
        install.Click += async delegate { await Apply("Install"); };
        remove.Click += async delegate
        {
            if (MessageBox.Show(this, "Remove CULTIC VR from the selected game folder? Saves and preferences will be kept.", "Uninstall CULTIC VR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await Apply("Remove");
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
    }

    private async Task Apply(string action)
    {
        string selected = path.Text;
        busy = true;
        path.Enabled = browse.Enabled = install.Enabled = remove.Enabled = false;
        status.Text = action == "Remove" ? "Removing CULTIC VR..." : "Installing CULTIC VR...";
        try
        {
            string result = await Task.Run(() => Installer.Run(action, selected));
            status.Text = action == "Remove" ? "CULTIC VR removed. Saves and preferences were kept." : "Ready to play. Make your headset available, then launch CULTIC through Steam.";
            status.AppendText(Environment.NewLine + Environment.NewLine + result);
        }
        catch (Exception e) { status.Text = "Could not complete the operation." + Environment.NewLine + Environment.NewLine + e.Message; }
        finally { busy = false; path.Enabled = browse.Enabled = install.Enabled = remove.Enabled = true; }
    }
}
