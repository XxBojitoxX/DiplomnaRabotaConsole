using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Spectre.Console;

namespace DiplomnaRabotaConsole
{
    public class FileNode
    {
        public string FullPath { get; set; } = string.Empty;
        public string Name => System.IO.Path.GetFileName(FullPath);
        public long SizeBytes { get; set; }
        public bool IsDirectory { get; set; }
        public List<FileNode> Children { get; set; } = new List<FileNode>();
        public string FormattedSize => IsDirectory ? "--" : $"{SizeBytes / 1024.0 / 1024.0:F2} MB";
    }

    public class ScannerService
    {
        public FileNode ScanDirectory(string path)
        {
            var node = new FileNode { FullPath = path, IsDirectory = true };
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var dir in dirInfo.GetDirectories())
                {
                    var child = ScanDirectory(dir.FullName);
                    node.Children.Add(child);
                    node.SizeBytes += child.SizeBytes;
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    node.Children.Add(new FileNode { FullPath = file.FullName, SizeBytes = file.Length, IsDirectory = false });
                    node.SizeBytes += file.Length;
                }
            }
            catch { /* Silently skip folders without permission */ }
            return node;
        }

        public void CopyPath(string source, string targetFolder)
        {
            try {
                if (System.IO.File.Exists(source))
                {
                    string dest = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(source));
                    System.IO.File.Copy(source, dest, true);
                }
                else if (System.IO.Directory.Exists(source))
                {
                    string name = new DirectoryInfo(source).Name;
                    CopyDirectory(source, System.IO.Path.Combine(targetFolder, name));
                }
            } catch (Exception ex) { AnsiConsole.WriteException(ex); }
        }

        private void CopyDirectory(string source, string dest)
        {
            System.IO.Directory.CreateDirectory(dest);
            foreach (var f in System.IO.Directory.GetFiles(source)) 
                System.IO.File.Copy(f, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(f)), true);
            foreach (var d in System.IO.Directory.GetDirectories(source)) 
                CopyDirectory(d, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(d)));
        }
    }

    class Program
    {
        private static ScannerService _scanner = new ScannerService();
        private static string? _clipboardPath = null;

        [STAThread]
        static void Main(string[] args)
        {
            // Helps the folder picker look modern
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            AnsiConsole.Write(new FigletText("File Manager").Color(Spectre.Console.Color.Cyan1));
            
            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Main Menu[/]")
                        .AddChoices(new[] { "Select Folder", "View Clipboard", "Exit" }));

                if (choice == "Exit") break;
                if (choice == "Select Folder") HandleScan();
                else if (choice == "View Clipboard")
                {
                    AnsiConsole.MarkupLine(_clipboardPath == null ? "[red]Clipboard is empty[/]" : $"[green]Copied:[/] {_clipboardPath}");
                    AnsiConsole.WriteLine("Press any key to continue...");
                    Console.ReadKey(true);
                }
            }
        }

        static void HandleScan()
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Pick a directory to scan";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FileNode? root = null;
                AnsiConsole.Status().Start("Scanning files...", ctx => {
                    root = _scanner.ScanDirectory(dialog.SelectedPath);
                });
                if (root != null) ShowBrowser(root);
            }
        }

        static void ShowBrowser(FileNode folder)
        {
            while (true)
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule($"[blue]Folder: {folder.Name}[/]"));

                // Dynamic height logic
                int dynamicPageSize = Math.Max(5, Console.WindowHeight - 8);

                var prompt = new SelectionPrompt<FileNode>()
                    .PageSize(dynamicPageSize)
                    .Title("[grey]Use Arrows to navigate, Enter to select, or Type to search[/]")
                    .EnableSearch() // NEW: Allows typing to filter the list!
                    .UseConverter(n => n.FullPath == ".." ? "[yellow].. (Back)[/]" : 
                                      (n.IsDirectory ? $"[blue]📁 {n.Name}[/]" : $"[white]📄 {n.Name}[/] [grey]({n.FormattedSize})[/]"));

                prompt.AddChoice(new FileNode { FullPath = ".." });
                prompt.AddChoices(folder.Children.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name));

                var selected = AnsiConsole.Prompt(prompt);
                if (selected.FullPath == "..") return;

                var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title($"[yellow]Actions for {selected.Name}:[/]")
                    .AddChoices(selected.IsDirectory ? 
                        new[] { "Open", "Copy Path", "Paste Into", "Delete", "Cancel" } : 
                        new[] { "Copy Path", "Delete", "Cancel" }));

                if (action == "Open") ShowBrowser(selected);
                else if (action == "Copy Path") { _clipboardPath = selected.FullPath; }
                else if (action == "Paste Into") { _scanner.CopyPath(_clipboardPath!, selected.FullPath); }
                else if (action == "Delete")
                {
                    if (AnsiConsole.Confirm($"[red]Delete {selected.Name}?[/]"))
                    {
                        try {
                            if (selected.IsDirectory) System.IO.Directory.Delete(selected.FullPath, true);
                            else System.IO.File.Delete(selected.FullPath);
                            folder.Children.Remove(selected);
                        } catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]"); Console.ReadKey(); }
                    }
                }
            }
        }
    }
}