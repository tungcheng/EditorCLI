using UnityEditor;

namespace Techies
{
    [EditorCommandClass]
    public class UnityCmd
    {
        [EditorCommand("set", "namespace", Description = "Set project root namespace", Usage = "set namespace <name>")]
        public string SetNamespace(string rootNamespace)
        {
            EditorSettings.projectGenerationRootNamespace = rootNamespace;
            return "root namespace is set: " + rootNamespace;
        }

        [EditorCommand("reset", "namespace", Description = "Reset project root namepsace", Usage = "reset namespace")]
        public string ResetNamespace()
        {
            EditorSettings.projectGenerationRootNamespace = null;
            return "root namespace is reset to empty";
        }

        [EditorCommand("get", "namespace", Description = "Get project root namespace", Usage = "get namespace")]
        public string GetNamespace()
        {
            var rootNamespace = EditorSettings.projectGenerationRootNamespace;
            if (string.IsNullOrEmpty(rootNamespace))
            {
                return $"root namespace is empty";
            }
            return $"current root namespace: {rootNamespace}";
        }
    }
}