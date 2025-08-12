using System;

namespace Techies
{
    /// <summary>
    /// Marks a class as a container for editor commands.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class EditorCommandClassAttribute : Attribute { }

    /// <summary>
    /// Marks a method as an editor command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class EditorCommandAttribute : Attribute
    {
        public string Scope { get; }
        public string Name { get; }
        public string Description { get; set; } = "No description provided.";
        public string Usage { get; set; } = "No usage information provided.";

        public EditorCommandAttribute(string scope, string name)
        {
            Scope = scope.ToLower();
            Name = name.ToLower();
        }
    }
}