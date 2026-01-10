using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace DiplomnaRabotaConsole
{
    // ==========================================
    // 1. THE DATA MODEL
    // ==========================================
    public class FileNode
    {
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int ItemCount { get; set; }
        public bool IsDirectory { get; set; }
        public List<FileNode> Children { get; set; } = new List<FileNode>();

        public string FormattedSize => $"{SizeBytes / 1024.0 / 1024.0:F2} MB";
    }

    // ==========================================
    // 2. THE LOGIC ENGINE
    // ==========================================
    public class ScannerService
    {
        public FileNode ScanDirectory(string path)
        {
            var node = new FileNode
            {
                FullPath = path,
                IsDirectory = true
            };

            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Scan Subdirectories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    var childNode = ScanDirectory(dir.FullName);
                    node.Children.Add(childNode);
                    node.SizeBytes += childNode.SizeBytes;
                    node.ItemCount += childNode.ItemCount;
                }

                // Scan Files
                foreach (var file in dirInfo.GetFiles())
                {
                    var fileNode = new FileNode
                    {
                        FullPath = file.FullName,
                        SizeBytes = file.Length,
                        ItemCount = 1,
                        IsDirectory = false
                    };
                    node.Children.Add(fileNode);
                    node.SizeBytes += fileNode.SizeBytes;
                    node.ItemCount++;
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"[!] Access Denied: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error scanning {path}: {ex.Message}");
            }

            return node;
        }

        public void DeletePath(string path)
        {
            // Check existence before trying to delete to avoid crashing on stale IDs
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
            else
            {
                throw new FileNotFoundException("Path not found (it might already be deleted)", path);
            }
        }

        public void CopyFile(string sourcePath, string targetFolder)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source file missing");
            if (!Directory.Exists(targetFolder)) throw new DirectoryNotFoundException("Target folder missing");

            string destPath = Path.Combine(targetFolder, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destPath, overwrite: true);
        }
    }

    // ==========================================
    // 3. THE PROGRAM (Now with Indexing!)
    // ==========================================
    class Program
    {
        private static ScannerService _scanner = new ScannerService();
        private static string? _clipboardPath = null;
        
        // This list remembers what we saw in the last scan so we can pick by ID
        private static List<FileNode> _sessionFiles = new List<FileNode>(); 

        static void Main(string[] args)
        {
            Console.WriteLine("=== FILE SCANNER CONSOLE v2.0 (Advanced) ===");

            while (true)
            {
                Console.WriteLine("\nCommands: [scan] [copy] [paste] [delete] [exit]");
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim().ToLower();

                switch (input)
                {
                    case "scan":
                        HandleScan();
                        break;
                    case "copy":
                        HandleCopy();
                        break;
                    case "paste":
                        HandlePaste();
                        break;
                    case "delete":
                        HandleDelete();
                        break;
                    case "exit":
                        return;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
                }
            }
        }

        static void HandleScan()
        {
            Console.Write("Enter folder to scan: ");
            string path = Console.ReadLine()?.Replace("\"", "") ?? "";

            if (Directory.Exists(path))
            {
                Console.WriteLine("Scanning... processing index.");
                
                // Clear the memory of the previous scan
                _sessionFiles.Clear();
                
                var root = _scanner.ScanDirectory(path);
                
                Console.WriteLine("\n--- Scan Results ---");
                Console.WriteLine("ID\tType\tSize\tName");
                Console.WriteLine("---------------------------------------------");
                
                // Start recursive printing and indexing
                PrintAndIndexTree(root, "");
                
                Console.WriteLine($"\nTotal: {root.FormattedSize} across {root.ItemCount} items.");
            }
            else
            {
                Console.WriteLine("Invalid directory.");
            }
        }

        // Modified to Flatten the list into _sessionFiles AND Print at the same time
        static void PrintAndIndexTree(FileNode node, string indent)
        {
            if (indent.Length > 12) return; // Prevent messy deep nesting

            // Add to our quick-access list
            _sessionFiles.Add(node);
            int id = _sessionFiles.Count - 1; // The index of this item

            string typeIcon = node.IsDirectory ? "[DIR]" : "[FILE]";
            Console.WriteLine($"{id}\t{typeIcon}\t{node.FormattedSize}\t{indent}+ {Path.GetFileName(node.FullPath)}");

            foreach (var child in node.Children)
            {
                // Removed the 'if Directory' check so you can see individual files now
                PrintAndIndexTree(child, indent + "  ");
            }
        }

        static void HandleCopy()
        {
            Console.Write("Enter path OR File ID to copy: ");
            string input = Console.ReadLine()?.Replace("\"", "") ?? "";

            string? selectedPath = ResolvePath(input);

            if (selectedPath != null)
            {
                _clipboardPath = selectedPath;
                Console.WriteLine($"Copied to clipboard: {_clipboardPath}");
            }
        }

        static void HandleDelete()
        {
            if (_sessionFiles.Count == 0)
            {
                Console.WriteLine("No files scanned yet. Run 'scan' first to get IDs.");
                // Fallback to manual path entry if they really want
            }

            Console.Write("Enter path OR ID to delete: ");
            string input = Console.ReadLine()?.Replace("\"", "") ?? "";

            string? pathToDelete = ResolvePath(input);

            if (pathToDelete == null) return;

            Console.Write($"⚠ WARNING: Are you sure you want to PERMANENTLY DELETE:\n   {pathToDelete}\n   (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
            {
                try {
                    _scanner.DeletePath(pathToDelete);
                    Console.WriteLine("Deleted successfully.");
                } catch (Exception ex) {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void HandlePaste()
        {
            if (string.IsNullOrEmpty(_clipboardPath))
            {
                Console.WriteLine("Clipboard empty. Use 'copy' first.");
                return;
            }

            Console.Write("Enter destination folder path OR ID: ");
            string input = Console.ReadLine()?.Replace("\"", "") ?? "";
            
            string? destPath = ResolvePath(input);

            if (destPath != null)
            {
                try {
                    // If they picked a file as destination, try to find its parent folder
                    if (File.Exists(destPath)) 
                    {
                        destPath = Path.GetDirectoryName(destPath) ?? destPath;
                    }

                    _scanner.CopyFile(_clipboardPath, destPath);
                    Console.WriteLine("Pasted successfully.");
                } catch (Exception ex) {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        // Helper to figure out if user typed "5" (ID) or "C:\Folder" (Path)
        static string? ResolvePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            // 1. Try to parse as an ID (Integer)
            if (int.TryParse(input, out int id))
            {
                if (id >= 0 && id < _sessionFiles.Count)
                {
                    return _sessionFiles[id].FullPath;
                }
                else
                {
                    Console.WriteLine($"ID {id} is out of range.");
                    return null;
                }
            }

            // 2. Otherwise treat it as a raw path
            return input;
        }
    }
}