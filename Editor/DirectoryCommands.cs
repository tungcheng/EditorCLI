using UnityEngine;
using System;
using System.IO;

[EditorCommandClass]
public class DirectoryCommands
{
    [EditorCommand("dir", "current", Description = "Shows the current working directory.")]
    public string CurrentDirectory()
    {
        return CommandLineEditor.Context.TryGetValue("CurrentDirectory", out var dir) 
            ? dir.ToString() 
            : "Error: CurrentDirectory not set in context.";
    }

    [EditorCommand("dir", "cd", Description = "Changes the current directory. Use '.' to go up.", Usage = "dir cd <path>")]
    public string ChangeDirectory(string path)
    {
        if (!CommandLineEditor.Context.TryGetValue("CurrentDirectory", out var currentDirObj))
        {
            return "Error: CurrentDirectory not set in context.";
        }
        
        string currentDir = currentDirObj.ToString();
        string newDir;

        if (path == ".")
        {
            var parent = Directory.GetParent(currentDir);
            if (parent == null || !parent.FullName.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return $"Error: Cannot navigate above project 'Assets' folder.";
            }
            newDir = parent.FullName;
        }
        else
        {
            newDir = Path.Combine(currentDir, path);
        }
        
        // Normalize path separators
        newDir = newDir.Replace("\\", "/");

        if (Directory.Exists(newDir))
        {
            CommandLineEditor.SetContext("CurrentDirectory", newDir);
            return $"Current directory is now: {newDir}";
        }
        else
        {
            return $"Error: Directory not found at '{newDir}'";
        }
    }
}