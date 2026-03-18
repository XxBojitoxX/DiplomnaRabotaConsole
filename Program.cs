using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace DiplomnaRabotaConsole
{
    public class FileNode
    {
        public string FullPath { get; set; } = string.Empty;
        public string Name => Path.GetFileName(FullPath) ?? FullPath;
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
            catch { /* Ignore access errors */ }
            return node;
        }

        public void CopyPath(string source, string targetFolder)
        {
            try {
                if (File.Exists(source))
                {
                    string dest = Path.Combine(targetFolder, Path.GetFileName(source));
                    File.Copy(source, dest, true);
                }
                else if (Directory.Exists(source))
                {
                    string name = new DirectoryInfo(source).Name;
                    CopyDirectory(source, Path.Combine(targetFolder, name));
                }
            } catch (Exception ex) { AnsiConsole.WriteException(ex); }
        }

        private void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(source)) 
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(source)) 
                CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
        }
    }

    class Program
    {
        private static ScannerService _scanner = new ScannerService();
        private static string? _clipboardPath = null;

        static void Main(string[] args)
        {
            AnsiConsole.Write(new FigletText("File Manager").Color(Spectre.Console.Color.Cyan1));
            
            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Main Menu[/]")
                        .AddChoices(new[] { "Select Folder to Scan", "View Clipboard", "Exit" }));

                if (choice == "Exit") break;
                if (choice == "Select Folder to Scan") HandleScan();
                else if (choice == "View Clipboard")
                {
                    AnsiConsole.MarkupLine(_clipboardPath == null ? "[red]Empty[/]" : $"[green]Copied:[/] {_clipboardPath}");
                    AnsiConsole.WriteLine("Press any key...");
                    Console.ReadKey(true);
                }
            }
        }

        static void HandleScan()
        {
            // Use our custom cross-platform terminal picker
            string? selectedPath = PickFolderTerminal();

            if (!string.IsNullOrEmpty(selectedPath))
            {
                FileNode? root = null;
                AnsiConsole.Status().Start("Scanning files...", ctx => {
                    root = _scanner.ScanDirectory(selectedPath);
                });
                if (root != null) ShowBrowser(root);
            }
        }

        // NEW: Cross-platform Terminal-based Folder Picker
        static string? PickFolderTerminal()
        {
            // Start at Home directory or Root
            string currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(currentPath)) currentPath = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/";

            while (true)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine($"[yellow]Select a Folder to Scan[/]");
                AnsiConsole.MarkupLine($"[blue]Current Path:[/] {currentPath}");
                AnsiConsole.Write(new Rule());

                var prompt = new SelectionPrompt<string>()
                    .PageSize(15)
                    .AddChoices("[bold green]>> SELECT THIS FOLDER <<[/]")
                    .AddChoices(".. (Back)");

                try 
                {
                    var dirs = Directory.GetDirectories(currentPath)
                                        .Select(d => "📁 " + Path.GetFileName(d))
                                        .OrderBy(d => d);
                    prompt.AddChoices(dirs);
                }
                catch { AnsiConsole.MarkupLine("[red]Access Denied[/]"); }

                var choice = AnsiConsole.Prompt(prompt);

                if (choice == "[bold green]>> SELECT THIS FOLDER <<[/]") return currentPath;
                
                if (choice == ".. (Back)")
                {
                    var parent = Directory.GetParent(currentPath);
                    if (parent != null) currentPath = parent.FullName;
                    else 
                    {
                        // On Windows, show drive letters if we hit the top
                        var drives = DriveInfo.GetDrives().Select(d => d.Name).ToList();
                        var driveChoice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Select Drive:").AddChoices(drives));
                        currentPath = driveChoice;
                    }
                }
                else
                {
                    currentPath = Path.Combine(currentPath, choice.Replace("📁 ", ""));
                }
            }
        }

        static void ShowBrowser(FileNode folder)
        {
            while (true)
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(new Rule($"[blue]Browsing: {folder.Name}[/]"));

                int dynamicPageSize = Math.Max(5, Console.WindowHeight - 8);

                var prompt = new SelectionPrompt<FileNode>()
                    .PageSize(dynamicPageSize)
                    .EnableSearch()
                    .UseConverter(n => n.FullPath == ".." ? "[yellow].. (Back)[/]" : 
                                      (n.IsDirectory ? $"[blue]📁 {n.Name}[/]" : $"[white]📄 {n.Name}[/] [grey]({n.FormattedSize})[/]"));

                prompt.AddChoice(new FileNode { FullPath = ".." });
                prompt.AddChoices(folder.Children.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name));

                var selected = AnsiConsole.Prompt(prompt);
                if (selected.FullPath == "..") return;

                var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .AddChoices(selected.IsDirectory ? 
                        new[] { "Open", "Copy Path", "Paste Into", "Delete", "Cancel" } : 
                        new[] { "Copy Path", "Delete", "Cancel" }));

                if (action == "Open") ShowBrowser(selected);
                else if (action == "Copy Path") _clipboardPath = selected.FullPath;
                else if (action == "Paste Into") _scanner.CopyPath(_clipboardPath!, selected.FullPath);
                else if (action == "Delete")
                {
                    if (AnsiConsole.Confirm("Delete this item?"))
                    {
                        if (selected.IsDirectory) Directory.Delete(selected.FullPath, true);
                        else File.Delete(selected.FullPath);
                        folder.Children.Remove(selected);
                    }
                }
            }
        }
    }
}