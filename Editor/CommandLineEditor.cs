using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;

// =======================================================================================
// 1. ATTRIBUTES for Command Discovery
// =======================================================================================

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

// =======================================================================================
// 2. MAIN EDITOR WINDOW
// =======================================================================================

public class CommandLineEditor : EditorWindow
{
    // --- Static Context & Command Storage ---
    private static readonly Dictionary<string, CommandInfo> _builtInCommands = new Dictionary<string, CommandInfo>();
    private static readonly Dictionary<string, Dictionary<string, CommandInfo>> _discoveredCommands = new Dictionary<string, Dictionary<string, CommandInfo>>();
    
    /// <summary>
    /// A read-only static context available to commands.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Context => _context;
    private static readonly Dictionary<string, object> _context = new Dictionary<string, object>();

    // --- UI Elements ---
    private VisualElement _infoArea;
    private Label _scopesLabel;
    private Label _directoryLabel;
    private TextField _commandInput;
    private ScrollView _outputArea;

    [MenuItem("Tools/Command Line Editor")]
    public static void ShowWindow()
    {
        CommandLineEditor wnd = GetWindow<CommandLineEditor>();
        wnd.titleContent = new GUIContent("Command Line");
    }

    private void OnEnable()
    {
        // Initialize context if it's the first time
        if (!_context.ContainsKey("CurrentDirectory"))
        {
            _context["CurrentDirectory"] = "Assets";
        }
        
        // Discover all commands when the window is enabled
        DiscoverAllCommands();
    }

    public void CreateGUI()
    {
        // --- Root Visual Element Setup ---
        var root = rootVisualElement;
        root.style.flexGrow = 1;
        root.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f); // Dark background
        root.RegisterCallback<MouseDownEvent>(evt => _commandInput?.Focus()); // Focus input on any click

        // --- A: Information Area ---
        _infoArea = new VisualElement();
        _scopesLabel = new Label { name = "scopes-label" };
        _directoryLabel = new Label { name = "directory-label" };
        _infoArea.Add(_scopesLabel);
        _infoArea.Add(_directoryLabel);
        root.Add(_infoArea);
        
        // --- B: Command Input Area ---
        _commandInput = new TextField
        {
            name = "command-input",
            value = ""
        };
        _commandInput.RegisterCallback<KeyDownEvent>(OnInputEnter);
        root.Add(_commandInput);

        // --- C: Output ScrollView Area ---
        _outputArea = new ScrollView(ScrollViewMode.Vertical)
        {
            name = "output-area"
        };
        _outputArea.style.flexGrow = 1; // Make it fill remaining space
        root.Add(_outputArea);

        // --- Apply Styles ---
        ApplyStyles();
        
        // --- Initial State ---
        UpdateInfoArea();
        AddOutputEntry("System", "Welcome to the Unity Command Line Editor. Type 'help' for commands.");

        // Schedule focus to ensure the UI is ready
        _commandInput.schedule.Execute(() => _commandInput.Focus()).StartingIn(100);
    }

    private void ApplyStyles()
    {
        // General
        var baseTextStyle = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
        var fontSize = 14;

        // Info Area
        _infoArea.style.paddingLeft = 5;
        _infoArea.style.paddingTop = 3;
        _infoArea.style.paddingBottom = 3;
        _infoArea.style.borderBottomWidth = 1;
        _infoArea.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
        
        _scopesLabel.style.color = new StyleColor(new Color(0.6f, 0.8f, 1f));
        _scopesLabel.style.fontSize = fontSize - 2;
        _directoryLabel.style.color = new StyleColor(new Color(0.6f, 1f, 0.8f));
        _directoryLabel.style.fontSize = fontSize - 2;

        // Input Field
        _commandInput.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        _commandInput.style.color = baseTextStyle;
        _commandInput.style.fontSize = fontSize;
        _commandInput.style.paddingLeft = 5;
        _commandInput.style.paddingRight = 5;
        _commandInput.style.minHeight = 22;

        // Output Area
        _outputArea.style.paddingLeft = 5;
        _outputArea.style.paddingRight = 5;
    }

    private void OnInputEnter(KeyDownEvent e)
    {
        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            string commandText = _commandInput.value;
            if (!string.IsNullOrWhiteSpace(commandText))
            {
                ExecuteCommand(commandText);
                _commandInput.value = ""; // Clear input
            }
            // Keep focus on the input field
            _commandInput.Focus();
            e.PreventDefault();
        }
    }

    private void AddOutputEntry(string command, string output)
    {
        var entryContainer = new VisualElement();
        entryContainer.style.marginTop = 5;
        entryContainer.style.marginBottom = 5;

        // Command Label (like "> echo hello")
        var commandLabel = new Label($"> {command}");
        commandLabel.style.color = new StyleColor(new Color(0.5f, 0.7f, 1f)); // Light blue
        commandLabel.style.fontSize = 14;
        commandLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
        commandLabel.selection.isSelectable = true;

        // Output Label
        var outputLabel = new Label(output);
        outputLabel.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
        outputLabel.style.fontSize = 14;
        outputLabel.style.whiteSpace = WhiteSpace.Normal; // Allow wrapping
        outputLabel.selection.isSelectable = true; // Make content copyable

        entryContainer.Add(commandLabel);
        entryContainer.Add(outputLabel);

        // Insert at the top for reverse chronological order
        _outputArea.Insert(0, entryContainer);
    }

    private void UpdateInfoArea()
    {
        var allScopes = _discoveredCommands.Keys.Distinct().OrderBy(s => s);
        _scopesLabel.text = $"Scopes: {string.Join(", ", allScopes)}";
        _directoryLabel.text = $"Directory: {(_context.TryGetValue("CurrentDirectory", out var dir) ? dir : "N/A")}";
    }

    // =======================================================================================
    // 3. COMMAND PROCESSING LOGIC
    // =======================================================================================

    private void ExecuteCommand(string commandText)
    {
        var parts = commandText.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string commandName = parts[0].ToLower();
        string output;

        // Check built-in commands first
        if (_builtInCommands.ContainsKey(commandName))
        {
            output = _builtInCommands[commandName].Invoke(parts.Skip(1).ToArray());
        }
        // Check discovered commands (scope command <args>)
        else if (parts.Length > 1 && _discoveredCommands.ContainsKey(commandName))
        {
            string scope = commandName;
            string subCommandName = parts[1].ToLower();
            if (_discoveredCommands[scope].ContainsKey(subCommandName))
            {
                output = _discoveredCommands[scope][subCommandName].Invoke(parts.Skip(2).ToArray());
            }
            else
            {
                output = $"Error: Command '{subCommandName}' not found in scope '{scope}'. Type 'help {scope}' for available commands.";
            }
        }
        else
        {
            output = $"Error: Command or scope '{commandName}' not found. Type 'help' for a list of commands and scopes.";
        }

        AddOutputEntry(commandText, output);
        UpdateInfoArea(); // Some commands might change the state (e.g., 'dir cd')
    }

    private static void DiscoverAllCommands()
    {
        // --- Clear and Register Built-in Commands ---
        _builtInCommands.Clear();
        RegisterBuiltInCommands();

        // --- Discover Attributed Commands ---
        _discoveredCommands.Clear();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<EditorCommandClassAttribute>() != null);

                foreach (var type in types)
                {
                    var instance = Activator.CreateInstance(type);
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                    foreach (var method in methods)
                    {
                        var attr = method.GetCustomAttribute<EditorCommandAttribute>();
                        if (attr == null) continue;

                        if (!_discoveredCommands.ContainsKey(attr.Scope))
                        {
                            _discoveredCommands[attr.Scope] = new Dictionary<string, CommandInfo>();
                        }

                        var commandInfo = new CommandInfo(instance, method, attr.Description, attr.Usage);
                        _discoveredCommands[attr.Scope][attr.Name] = commandInfo;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CommandLineEditor] Could not discover commands in assembly {assembly.GetName().Name}: {ex.Message}");
            }
        }
    }

    private static void RegisterBuiltInCommands()
    {
        // Help
        _builtInCommands["help"] = new CommandInfo((args) =>
        {
            if (args.Length == 0)
            {
                var helpText = "Available Built-in Commands:\n" +
                               string.Join("\n", _builtInCommands.Select(kvp => $"  {kvp.Key,-10} - {kvp.Value.Description}")) +
                               "\n\nAvailable Scopes (use 'help <scope>' for details):\n" +
                               string.Join("\n", GetAvailableScopes().Select(s => $"  {s}"));
                return helpText;
            }
            else
            {
                string scope = args[0].ToLower();
                if (_discoveredCommands.TryGetValue(scope, out var scopeCommands))
                {
                    var scopeHelp = $"Commands in scope '{scope}':\n" +
                                    string.Join("\n", scopeCommands.Select(kvp => $"  {kvp.Key,-15} - {kvp.Value.Description}\n    Usage: {kvp.Value.Usage}"));
                    return scopeHelp;
                }
                return $"Error: Scope '{scope}' not found.";
            }
        }, "Shows available commands or help for a specific scope.");

        // Clear
        _builtInCommands["clear"] = new CommandInfo((args) =>
        {
            // The actual clearing happens in the window instance, this just returns a confirmation.
            var window = GetWindow<CommandLineEditor>();
            window._outputArea.Clear();
            return "Output cleared.";
        }, "Clears the output area.");

        // Log
        _builtInCommands["log"] = new CommandInfo((args) =>
        {
            var message = string.Join(" ", args);
            Debug.Log($"[CommandLineEditor] {message}");
            return $"Message logged to Unity Console: {message}";
        }, "Logs a message to the Unity Console.");

        // Echo
        _builtInCommands["echo"] = new CommandInfo((args) => string.Join(" ", args), "Prints the given text back to the output.");

        // Refresh
        _builtInCommands["refresh"] = new CommandInfo((args) =>
        {
            DiscoverAllCommands();
            return "Command list refreshed.";
        }, "Re-scans all assemblies for commands.");
        
        // Scopes
        _builtInCommands["scopes"] = new CommandInfo((args) =>
        {
            return "Available Scopes:\n" + string.Join("\n", GetAvailableScopes().Select(s => $"  {s}"));
        }, "Lists all available command scopes.");
    }

    private static IEnumerable<string> GetAvailableScopes()
    {
        return _discoveredCommands.Keys.Distinct().OrderBy(s => s);
    }

    // --- Helper class for storing command data ---
    private class CommandInfo
    {
        private readonly object _instance;
        private readonly MethodInfo _methodInfo;
        private readonly Func<string[], string> _builtInFunc;
        public string Description { get; }
        public string Usage { get; }

        // Constructor for discovered commands
        public CommandInfo(object instance, MethodInfo methodInfo, string description, string usage)
        {
            _instance = instance;
            _methodInfo = methodInfo;
            Description = description;
            Usage = usage;
        }

        // Constructor for built-in commands
        public CommandInfo(Func<string[], string> builtInFunc, string description)
        {
            _builtInFunc = builtInFunc;
            Description = description;
            Usage = "Varies by command.";
        }

        public string Invoke(string[] args)
        {
            try
            {
                if (_builtInFunc != null)
                {
                    return _builtInFunc(args);
                }

                var parameters = _methodInfo.GetParameters();
                if (args.Length != parameters.Length)
                {
                    return $"Error: Invalid number of arguments. Expected {parameters.Length}, got {args.Length}.\nUsage: {Usage}";
                }

                var convertedArgs = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    convertedArgs[i] = Convert.ChangeType(args[i], parameters[i].ParameterType);
                }

                var result = _methodInfo.Invoke(_instance, convertedArgs);
                return result?.ToString() ?? "Command executed successfully (no output).";
            }
            catch (Exception ex)
            {
                return $"Execution Error: {ex.InnerException?.Message ?? ex.Message}";
            }
        }
    }
    
    /// <summary>
    /// Public method to modify the static context.
    /// </summary>
    public static void SetContext(string key, object value)
    {
        _context[key] = value;
    }
}

// =======================================================================================
// 4. EXAMPLE COMMANDS
// =======================================================================================

[EditorCommandClass]
public class SampleCommands
{
    [EditorCommand("math", "add", Description = "Adds two integer numbers.", Usage = "math add <num1> <num2>")]
    public string Add(int a, int b)
    {
        return $"{a} + {b} = {a + b}";
    }

    [EditorCommand("math", "multiply", Description = "Multiplies two float numbers.", Usage = "math multiply <num1> <num2>")]
    public string Multiply(float a, float b)
    {
        return $"{a} * {b} = {a * b}";
    }
}

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