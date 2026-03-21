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
        public string Name => Path.GetFileName(FullPath) == "" ? FullPath : Path.GetFileName(FullPath);
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
                    // For performance, we only scan the top level here
                    // or you can recurse if the folder is small.
                    node.Children.Add(new FileNode { FullPath = dir.FullName, IsDirectory = true });
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    node.Children.Add(new FileNode { FullPath = file.FullName, SizeBytes = file.Length, IsDirectory = false });
                    node.SizeBytes += file.Length;
                }
            }
            catch (Exception) { /* Swallow access errors */ }
            return node;
        }

        public void CopyPath(string source, string targetFolder)
        {
            string dest = Path.Combine(targetFolder, Path.GetFileName(source));
            if (File.Exists(source)) File.Copy(source, dest, true);
            else if (Directory.Exists(source)) CopyDirectory(source, dest);
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
        private static bool _isMoveOperation = false;

        static void Main(string[] args)
        {
            Console.Clear();
            AnsiConsole.Write(new FigletText("File Manager").Color(Color.Cyan1));

            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Main Menu[/]")
                        .AddChoices(new[] { "Select Folder to Scan", "Manual Path Search", "View Clipboard", "Exit" }));

                if (choice == "Exit") 
                {
                    AnsiConsole.Write(new Rule("Thank you for checking out my program!")); 
                    AnsiConsole.WriteLine(); // Add some spacing
                    AnsiConsole.Write(new Rule("[yellow]Press any key to exit[/]").Centered());
                    Console.ReadKey(true); 
                    break;
                }
                if (choice == "Select Folder to Scan")
                {
                    string? path = PickFolderTerminal();
                    if (path != null) StartBrowser(path);
                }
                if (choice == "Manual Path Search")
                {
                    string path = AnsiConsole.Ask<string>("Enter path:");
                    if (Directory.Exists(path)) StartBrowser(path);
                    else AnsiConsole.MarkupLine("[red]Invalid Directory![/]");
                }
                if (choice == "View Clipboard")
                {
                    AnsiConsole.MarkupLine(_clipboardPath == null ? "[red]Clipboard Empty. Press any key to go back to the main menu.[/]" : $"[yellow]Pending:[/] {_clipboardPath}");
                    Console.ReadKey();
                }
            }
        }

        static void StartBrowser(string path)
        {
            FileNode root = _scanner.ScanDirectory(path);
            ShowBrowser(root);
        }

        static string? PickFolderTerminal()
        {
            string currentPath = Directory.GetCurrentDirectory();
            while (true)
            {
                Console.Clear();
                var dirs = Directory.GetDirectories(currentPath).Select(d => $"📁 {Path.GetFileName(d)}").ToList();
                
                var prompt = new SelectionPrompt<string>()
                    .Title($"Pick a folder: [green]{currentPath}[/]")
                    .AddChoices("[bold green]>> SELECT THIS FOLDER <<[/]", ".. (Back)")
                    .AddChoices(dirs);

                var choice = AnsiConsole.Prompt(prompt);

                if (choice == "[bold green]>> SELECT THIS FOLDER <<[/]") return currentPath;
                if (choice == ".. (Back)") 
                    currentPath = Directory.GetParent(currentPath)?.FullName ?? currentPath;
                else 
                    currentPath = Path.Combine(currentPath, choice.Replace("📁 ", ""));
            }
        }

        static void ShowBrowser(FileNode currentFolder)
        {
            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(new Rule($"[blue]Browsing: {currentFolder.FullPath}[/]"));

                var nodeSelection = new SelectionPrompt<FileNode>()
                    .PageSize(15)
                    .UseConverter(n => n.FullPath == "BACK" ? "[yellow]<- Return to Menu[/]" :
                                      (n.IsDirectory ? $"[blue]📁 {n.Name}[/]" : $"[white]📄 {n.Name}[/] [grey]({n.FormattedSize})[/]"));

                nodeSelection.AddChoice(new FileNode { FullPath = "BACK" });
                
                // Re-scan to get fresh data
                var freshNode = _scanner.ScanDirectory(currentFolder.FullPath);
                nodeSelection.AddChoices(freshNode.Children.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name));

                var selected = AnsiConsole.Prompt(nodeSelection);
                if (selected.FullPath == "BACK") return;

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"Action for: {selected.Name}")
                        .AddChoices(selected.IsDirectory ? new[] { "Open", "Copy", "Move", "Paste Here", "Delete", "Cancel" } 
                                                       : new[] { "Copy", "Move", "Delete", "Cancel" }));

                switch (action)
                {
                    case "Open":
                        ShowBrowser(selected);
                        break;
                    case "Copy":
                        _clipboardPath = selected.FullPath;
                        _isMoveOperation = false;
                        break;
                    case "Move":
                        _clipboardPath = selected.FullPath;
                        _isMoveOperation = true;
                        break;
                    case "Paste Here":
                        if (_clipboardPath != null)
                        {
                            if (_isMoveOperation) Directory.Move(_clipboardPath, Path.Combine(currentFolder.FullPath, Path.GetFileName(_clipboardPath)));
                            else _scanner.CopyPath(_clipboardPath, currentFolder.FullPath);
                            _clipboardPath = null;
                        }
                        break;
                    case "Delete":
                        if (AnsiConsole.Confirm("Are you sure?"))
                        {
                            if (selected.IsDirectory) Directory.Delete(selected.FullPath, true);
                            else File.Delete(selected.FullPath);
                        }
                        break;
                }
            }
        }
    }
}