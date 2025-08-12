namespace Techies
{
    [EditorCommandClass]
    public class OpenUpmCmd
    {
        // openupm add com.my.package@1.2.3
        [EditorCommand("openupm", "add", Description = "Add package com.my.package@1.2.3", Usage = "openupm add <package>@<version>")]
        public string Add(string package)
        {
            return "not implemented";
        }

        [EditorCommand("openupm", "remove", Description = "Remove package com.my.package", Usage = "openupm add <package>")]
        public string Remove(string package)
        {
            return "not implemented";
        }
    }
}