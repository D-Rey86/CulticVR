using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class InstallerUiTests
{
    private static void PrepareControls(Control control)
    {
        IntPtr handle = control.Handle;
        control.PerformLayout();
        foreach (Control child in control.Controls) PrepareControls(child);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            foreach (string value in new[] { "", @"C:\Steam Library\CULTIC", @"C:\O'Brien & Games\CULTIC", @"C:\trailing\", "quoted\"text", @"\\server\share\game" })
            {
                int count;
                IntPtr result = CommandLineToArgvW("installer " + Installer.Quote(value), out count);
                try
                {
                    if (count != 2 || Marshal.PtrToStringUni(Marshal.ReadIntPtr(result, IntPtr.Size)) != value)
                        throw new Exception("Windows argument quoting failed.");
                }
                finally { LocalFree(result); }
            }
            string root = Path.Combine(Path.GetFullPath(args[0]), "Fake Steam");
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\Steam Games\" } \"1\" { \"path\" \"E:\\\\Games\" } }");
            string[] libraries = Installer.LibraryPaths(root).ToArray();
            if (!libraries.Contains(@"D:\Steam Games") || !libraries.Contains(@"E:\Games") || !libraries.Contains(root))
                throw new Exception("Steam library discovery failed.");
            Application.EnableVisualStyles();
            using (var form = new InstallWindow())
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                // Offscreen only; never show a desktop window during verification.
                PrepareControls(form); form.PerformLayout();
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(Path.GetFullPath(args[0]), "installer-preview.png"));
            }
            Console.WriteLine("PASS: Windows argument quoting, Steam library discovery and offscreen installer rendering.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
