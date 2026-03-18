using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace DiplomnaRabotaConsole
{
    // ==========================================
    // 1. THE DATA MODEL
    // ==========================================
    public class FileNode
    {
        public string FullPath { get; set; } = string.Empty;
        public string Name => Path.GetFileName(FullPath) ?? FullPath;
        public long SizeBytes { get; set; }
        public bool IsDirectory { get; set; }
        public List<FileNode> Children { get; set; } = new List<FileNode>();
        public string FormattedSize => IsDirectory ? "--" : $"{SizeBytes / 1024.0 / 1024.0:F2} MB";
    }

    // ==========================================
    // 2. THE LOGIC ENGINE
    // ==========================================
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
            catch { /* Skip unauthorized folders */ }
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

        // INTEGRATED: Your MovePath logic
        public void MovePath(string source, string destination)
        {
            try {
                if (File.Exists(source)) 
                    File.Move(source, Path.Combine(destination, Path.GetFileName(source)));
                else if (Directory.Exists(source)) 
                    Directory.Move(source, Path.Combine(destination, new DirectoryInfo(source).Name));
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

    // ==========================================
    // 3. THE INTERFACE
    // ==========================================
    class Program
    {
        private static ScannerService _scanner = new ScannerService();
        private static string? _clipboardPath = null;
        private static bool _isMoveOperation = false; // Track if we are moving or copying

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
                    string opType = _isMoveOperation ? "[yellow]MOVE[/]" : "[blue]COPY[/]";
                    AnsiConsole.MarkupLine(_clipboardPath == null ? "[red]Empty[/]" : $"{opType} [green]Path:[/] {_clipboardPath}");
                    AnsiConsole.WriteLine("Press any key...");
                    Console.ReadKey(true);
                }
            }
        }

        static void HandleScan()
        {
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

        static string? PickFolderTerminal()
        {
            string currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(currentPath)) currentPath = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/";

            while (true)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine($"[yellow]Browse to Folder[/]");
                AnsiConsole.MarkupLine($"[blue]Current Path:[/] {currentPath}");
                AnsiConsole.Write(new Rule());

                var prompt = new SelectionPrompt<string>()
                    .PageSize(15)
                    .AddChoices("[bold green]>> SELECT THIS FOLDER <<[/]")
                    .AddChoices(".. (Back)");

                try {
                    var dirs = Directory.GetDirectories(currentPath).Select(d => "📁 " + Path.GetFileName(d)).OrderBy(d => d);
                    prompt.AddChoices(dirs);
                } catch { AnsiConsole.MarkupLine("[red]Access Denied[/]"); }

                var choice = AnsiConsole.Prompt(prompt);
                if (choice == "[bold green]>> SELECT THIS FOLDER <<[/]") return currentPath;
                
                if (choice == ".. (Back)")
                {
                    var parent = Directory.GetParent(currentPath);
                    if (parent != null) currentPath = parent.FullName;
                    else {
                        var drives = DriveInfo.GetDrives().Select(d => d.Name).ToList();
                        currentPath = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Select Drive:").AddChoices(drives));
                    }
                }
                else currentPath = Path.Combine(currentPath, choice.Replace("📁 ", ""));
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

                var actionChoices = new List<string> { "Copy", "Move", "Delete", "Cancel" };
                if (selected.IsDirectory) { actionChoices.Insert(0, "Open"); actionChoices.Insert(3, "Paste Here"); }

                var action = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices(actionChoices));

                if (action == "Open") ShowBrowser(selected);
                else if (action == "Copy") { _clipboardPath = selected.FullPath; _isMoveOperation = false; }
                else if (action == "Move") { _clipboardPath = selected.FullPath; _isMoveOperation = true; }
                else if (action == "Paste Here") 
                {
                    if (_clipboardPath != null)
                    {
                        if (_isMoveOperation) _scanner.MovePath(_clipboardPath, selected.FullPath);
                        else _scanner.CopyPath(_clipboardPath, selected.FullPath);
                        _clipboardPath = null; // Clear clipboard after move
                        AnsiConsole.MarkupLine("[green]Operation Complete![/]");
                        Console.ReadKey();
                    }
                }
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