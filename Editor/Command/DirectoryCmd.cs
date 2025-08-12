using System;

namespace Techies
{
    [EditorCommandClass]
    public class DirectoryCmd
    {
        [EditorCommand("dir", "current", Description = "Shows the current working directory.", Usage = "dir current")]
        public string CurrentDirectory()
        {
            return $"Current directory is: \"{CommandLineEditor.GetCurrentDirectory()}\"";
        }

        [EditorCommand("dir", "reset", Description = "Resets the current directory to the project 'Assets' folder.", Usage = "dir reset")]
        public string ResetDirectory()
        {
            CommandLineEditor.ResetCurrentDirectory();
            return $"Current directory reset to: {CommandLineEditor.GetCurrentDirectory()}";
        }

        [EditorCommand("dir", "cd", Description = "Changes the current directory. Use '.' to go up.", Usage = "dir cd <path>")]
        public string ChangeDirectory(string path)
        {
            if (path == ".")
            {
                if (!CommandLineEditor.SetCurrentDirectoryUp(out string errorMessage))
                {
                    return errorMessage;
                }
                return CurrentDirectory();
            }

            var directories = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (directories.Length == 1)
            {
                if (!CommandLineEditor.SetCurrentDirectoryDown(directories[0], out string errorMessage))
                {
                    return errorMessage;
                }
                return CurrentDirectory();
            }

            if (directories.Length > 1)
            {
                if (!CommandLineEditor.SetCurrentDirectory(directories, out string errorMessage))
                {
                    return errorMessage;
                }

                return CurrentDirectory();
            }

            return "Error: Invalid path format. Use 'dir cd <path>' or 'dir cd .'";
        }
    }
}