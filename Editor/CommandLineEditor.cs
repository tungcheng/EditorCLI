using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Techies
{
    public class CommandLineEditor : EditorWindow
    {
        // --- Static Context & Command Storage ---
        private static readonly Dictionary<string, CommandInfo> _builtInCommands = new Dictionary<string, CommandInfo>();
        private static readonly Dictionary<string, Dictionary<string, CommandInfo>> _discoveredCommands = new Dictionary<string, Dictionary<string, CommandInfo>>();
        private List<string> _commandHistory;
        private int _historyIndex = -1;

        private static List<string> _directoryPath = new List<string>();

        class OutputEntry
        {
            public string Command { get; set; }
            public string Output { get; set; }
        }

        private List<OutputEntry> _outputEntries;

        // --- UI Elements ---
        private VisualElement _infoArea;
        private Label _scopesLabel;
        private Label _directoryLabel;
        private TextField _commandInput;
        private ScrollView _outputArea;

        [MenuItem("Window/Command Line Editor")]
        public static void ShowWindow()
        {
            CommandLineEditor wnd = GetWindow<CommandLineEditor>();
            wnd.titleContent = new GUIContent("Command Line");
        }

        private void OnEnable()
        {
            DiscoverAllCommands();
        }

        private void OnGUI()
        {
            HandleSpecialKeyInput();
        }

        private void HandleSpecialKeyInput()
        {
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown || currentEvent.type == EventType.KeyUp)
            {
                if (currentEvent.keyCode == KeyCode.UpArrow ||
                    currentEvent.keyCode == KeyCode.DownArrow ||
                    currentEvent.keyCode == KeyCode.LeftArrow ||
                    currentEvent.keyCode == KeyCode.RightArrow ||
                    currentEvent.keyCode == KeyCode.Return ||
                    currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    currentEvent.Use();
                    SetFocusCommandInput(null);
                }
            }
        }

        private void CreateGUI()
        {
            // --- Root Visual Element Setup ---
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f); // Dark background
            root.RegisterCallback<MouseDownEvent>(SetFocusCommandInput, TrickleDown.NoTrickleDown); // Focus input on any click

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
                value = "",
                selectAllOnFocus = false,
                selectAllOnMouseUp = false,
                multiline = false,
            };
#if UNITY_6000_0_OR_NEWER
            _commandInput.textEdition.placeholder = "Type a command and press Enter...";
            _commandInput.textEdition.hidePlaceholderOnFocus = false;
#endif
            _commandInput.RegisterCallback<KeyDownEvent>(OnInputEnter, TrickleDown.TrickleDown);
            root.Add(_commandInput);

            // --- C: Output ScrollView Area ---
            _outputArea = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "output-area",
            };
            _outputArea.style.flexGrow = 1; // Make it fill remaining space
            root.Add(_outputArea);

            // --- Apply Styles ---
            ApplyStyles();

            // --- Initial State ---
            UpdateInfoArea();

            LoadHistory();
            if (_outputEntries.Count > 0)
            {
                foreach (var entry in _outputEntries)
                {
                    AddOutputEntry(entry.Command, entry.Output, false);
                }
            }
            else
            {
                AddOutputEntry("System", "Welcome to the Unity Command Line Editor. Type 'help' for commands.");
            }

            SetFocusCommandInput(null);
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
                    _commandInput.value = ""; // Clear input after execution
                }
                SetFocusCommandInput(e);
                return;
            }
            if (e.keyCode == KeyCode.UpArrow)
            {
                NavigateHistory(-1); // Navigate to previous command
                SetFocusCommandInput(e);
                return;
            }
            if (e.keyCode == KeyCode.DownArrow)
            {
                NavigateHistory(1); // Navigate to next command
                SetFocusCommandInput(e);
                return;
            }
        }

        private void SetFocusCommandInput(EventBase evt)
        {
            var inputField = _commandInput.Q("unity-text-input");
            evt?.StopPropagation();
            inputField.Focus();
            _commandInput.cursorIndex = _commandInput.value.Length;
            _commandInput.selectIndex = _commandInput.value.Length;
            _commandInput.schedule.Execute(() =>
            {
                inputField.Focus();
                _commandInput.cursorIndex = _commandInput.value.Length;
                _commandInput.selectIndex = _commandInput.value.Length;
            })
            .StartingIn(0);
        }

        private void AddOutputEntry(string command, string output, bool isSaved = true)
        {
            if (isSaved)
            {
                SaveHistory(command, output);
            }

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
            _directoryLabel.text = $"Directory: \"{GetCurrentDirectory()}\"";
        }

        // =======================================================================================
        // 3. COMMAND PROCESSING LOGIC
        // =======================================================================================

        private string[] GetCommandParts(string commandText)
        {
            var args = new List<string>();
            var currentArg = new StringBuilder();

            for (int i = 0; i < commandText.Length; i++)
            {
                char c = commandText[i];
                if (currentArg.Length > 0)
                {
                    if (c == ' ')
                    {
                        args.Add(currentArg.ToString());
                        currentArg.Clear();
                        continue;
                    }
                }

                if (currentArg.Length == 0 && c == '"')
                {
                    i++;
                    while (i < commandText.Length)
                    {
                        if (commandText[i] == '"')
                        {
                            break;
                        }
                        currentArg.Append(commandText[i]);
                        i++;
                    }
                    continue;
                }

                currentArg.Append(c);
            }

            if (currentArg.Length > 0)
            {
                args.Add(currentArg.ToString());
            }

            return args.ToArray();
        }

        private void ExecuteCommand(string commandText)
        {
            var parts = GetCommandParts(commandText);
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

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;

            _historyIndex += direction;
            _historyIndex = Mathf.Clamp(_historyIndex, 0, _commandHistory.Count - 1);

            _commandInput.value = _commandHistory[_historyIndex];
        }

        private void DiscoverAllCommands()
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

        private void RegisterBuiltInCommands()
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
                ClearHistory();
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

        private void LoadHistory()
        {
            _historyIndex = EditorPrefs.GetInt("CommandLineEditor.HistoryIndex", -1);
            _commandHistory = JsonConvert.DeserializeObject<List<string>>(EditorPrefs.GetString("CommandLineEditor.History", "[]")) ?? new List<string>();
            _outputEntries = JsonConvert.DeserializeObject<List<OutputEntry>>(EditorPrefs.GetString("CommandLineEditor.Output", "[]")) ?? new List<OutputEntry>();
        }

        private void ClearHistory()
        {
            _commandHistory.Clear();
            _historyIndex = -1;
            EditorPrefs.DeleteKey("CommandLineEditor.History");
            EditorPrefs.DeleteKey("CommandLineEditor.HistoryIndex");
            _outputEntries.Clear();
            EditorPrefs.DeleteKey("CommandLineEditor.Output");
        }

        private void SaveHistory(string command, string output)
        {
            if (command == "clear") return;

            if (_commandHistory.Contains(command))
            {
                _commandHistory.Remove(command);
            }

            if (command != "System" && command != "clear")
            {
                _commandHistory.Add(command);
                _historyIndex = _commandHistory.Count;
                EditorPrefs.SetString("CommandLineEditor.History", JsonConvert.SerializeObject(_commandHistory));
                EditorPrefs.SetInt("CommandLineEditor.HistoryIndex", _historyIndex);
            }

            _outputEntries.Add(new OutputEntry { Command = command, Output = output });
            EditorPrefs.SetString("CommandLineEditor.Output", JsonConvert.SerializeObject(_outputEntries));
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

        public static string GetBaseDirectory()
        {
            return Directory.GetCurrentDirectory();
        }

        public static string GetAssetsDirectoryFulPath()
        {
            return Path.Combine(GetBaseDirectory(), "Assets");
        }

        private static string GetDirectoryPath(IEnumerable<string> directoryPath)
        {
            return string.Join("/", directoryPath) + (directoryPath.Any() ? "/" : "");
        }

        private static string GetDirectoryFullPath(IEnumerable<string> directoryPath)
        {
            return Path.Combine(GetAssetsDirectoryFulPath(), GetDirectoryPath(directoryPath));
        }

        public static string GetCurrentDirectory()
        {
            return "Assets/" + GetDirectoryPath(_directoryPath);
        }

        public static string GetCurrentDirectoryFullPath()
        {
            return Path.Combine(GetBaseDirectory(), GetCurrentDirectory());
        }

        public static void ResetCurrentDirectory()
        {
            _directoryPath.Clear();
        }

        public static bool SetCurrentDirectoryUp(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (_directoryPath.Count == 0)
            {
                errorMessage = $"Error: Cannot navigate above project 'Assets' folder.";
                return false;
            }
            _directoryPath.RemoveAt(_directoryPath.Count - 1);
            return true;
        }

        public static bool SetCurrentDirectoryDown(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrEmpty(directory))
            {
                errorMessage = "Error: Cannot set current directory to an empty string.";
                return false;
            }
            if (!Directory.Exists(Path.Combine(GetCurrentDirectory(), directory)))
            {
                errorMessage = $"Error: Directory '{directory}' does not exist in the current path '{GetCurrentDirectory()}'.";
                return false;
            }
            _directoryPath.Add(directory);
            return true;
        }

        public static bool SetCurrentDirectory(string[] directoryPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            var fullPath = GetDirectoryFullPath(directoryPath);
            if (!Directory.Exists(fullPath))
            {
                errorMessage = $"Error: Directory '{fullPath}' does not exist.";
                return false;
            }
            ResetCurrentDirectory();
            _directoryPath.AddRange(directoryPath);
            return true;
        }
    }
}