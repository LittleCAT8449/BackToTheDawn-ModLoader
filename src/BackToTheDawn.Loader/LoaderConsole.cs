using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.Loader;

/// <summary>
/// Small in-game command console for loader diagnostics and catalog work.
/// Toggle with F8. Commands operate on the public catalog and inventory APIs;
/// inventory mutation commands affect the current game state and may be saved.
/// </summary>
public sealed class LoaderConsole : MonoBehaviour
{
    private const string InputControlName = "BackToTheDawn.LoaderConsole.Input";
    private const int MaxLines = 160;

    private readonly List<string> _lines = new();
    private Vector2 _scrollPosition;
    private string _input = string.Empty;
    private bool _visible;
    private bool _focusInput;

    public LoaderConsole(IntPtr pointer) : base(pointer)
    {
    }

    private void Start()
    {
        WriteLine("Loader console ready. Press F8 to toggle; type help for commands.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            _visible = !_visible;
            _focusInput = _visible;
        }
    }

    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        var width = Mathf.Min(Screen.width - 32f, 760f);
        var height = Mathf.Min(Screen.height - 32f, 480f);
        var area = new Rect(16f, 16f, width, height);
        GUI.Box(area, "Back To The Dawn Loader Console (F8)");

        GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 28f, area.width - 24f, area.height - 42f));
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
        foreach (var line in _lines)
        {
            GUILayout.Label(line);
        }

        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName(InputControlName);
        _input = GUILayout.TextField(_input, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Run", GUILayout.Width(60f)))
        {
            ExecuteCommand();
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (_focusInput)
        {
            GUI.FocusControl(InputControlName);
            _focusInput = false;
        }

        if (Event.current.type == EventType.KeyDown &&
            Event.current.keyCode == KeyCode.Return &&
            GUI.GetNameOfFocusedControl() == InputControlName)
        {
            ExecuteCommand();
            Event.current.Use();
        }
    }

    private void ExecuteCommand()
    {
        var commandLine = _input.Trim();
        _input = string.Empty;
        if (commandLine.Length == 0)
        {
            return;
        }

        WriteLine("> " + commandLine);
        var parts = commandLine.Split(
            ' ',
            3,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        switch (command)
        {
            case "help":
                WriteLine("help | items [filter] | item get <key> | item register <key> <name>");
                WriteLine("item inject (experimental; changes live c_item only)");
                WriteLine("item give <key> [count] (uses Inventory API; changes live inventory)");
                WriteLine("inventory [add|remove] <key> [count] | inventory (show snapshot)");
                WriteLine("rooms | rooms get <key> | rooms goto <key>");
                WriteLine("rooms unregister <key> | rooms unregister-all <modId>");
                WriteLine("mods | state | clear");
                break;
            case "items":
                ListItems(parts.Length > 1 ? parts[1] : string.Empty);
                break;
            case "item":
                ExecuteItemCommand(
                    parts.Length > 1 ? parts[1] : string.Empty,
                    parts.Length > 2 ? parts[2] : string.Empty);
                break;
            case "inventory":
                ExecuteInventoryCommand(
                    parts.Length > 1 ? parts[1] : string.Empty,
                    parts.Length > 2 ? parts[2] : string.Empty);
                break;
            case "rooms":
                ExecuteRoomCommand(
                    parts.Length > 1 ? parts[1] : string.Empty,
                    parts.Length > 2 ? parts[2] : string.Empty);
                break;
            case "mods":
                ListMods();
                break;
            case "state":
                ShowState();
                break;
            case "clear":
                _lines.Clear();
                break;
            default:
                WriteLine("Unknown command. Type help.");
                break;
        }
    }

    private void ExecuteItemCommand(string subcommand, string argumentText)
    {
        if (string.IsNullOrWhiteSpace(subcommand))
        {
            WriteLine(
                "Usage: item get <key> | item register <key> <name> | " +
                "item inject | item give <key> [count]");
            return;
        }

        subcommand = subcommand.ToLowerInvariant();
        if (subcommand == "get" || subcommand == "effects")
        {
            if (string.IsNullOrWhiteSpace(argumentText))
            {
                WriteLine("Usage: item get <namespace:path>");
                return;
            }

            ShowItem(argumentText);
            return;
        }

        if (subcommand == "register")
        {
            if (string.IsNullOrWhiteSpace(argumentText))
            {
                WriteLine("Usage: item register <namespace:path> <display name>");
                return;
            }

            var arguments = argumentText.Split(
                ' ',
                2,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (arguments.Length < 2 || !ItemKey.TryParse(arguments[0], out var key))
            {
                WriteLine("A valid namespaced key and display name are required.");
                return;
            }

            var item = new ConsoleItem(key, arguments[1]);
            var registration = item.Register();
            WriteLine(
                registration.Succeeded
                    ? $"Registered virtual item {key}."
                    : $"Could not register {key}: {registration.Status} - {registration.Message}");
            return;
        }

        if (subcommand == "inject")
        {
            var count = RuntimeItemInjection.TryInject(true);
            WriteLine($"Injected {count} Mod item(s) into the live c_item table.");
            return;
        }

        if (subcommand == "give")
        {
            GiveItem(argumentText);
            return;
        }

        WriteLine("Unknown item command. Use item get, item register, item inject, or item give.");
    }

    private void ListItems(string filter)
    {
        var matches = ItemCatalog.All
            .Where(item => string.IsNullOrWhiteSpace(filter) ||
                           item.Key.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Take(32)
            .ToArray();
        WriteLine($"Items: {ItemCatalog.All.Count} total, showing {matches.Length}.");
        foreach (var item in matches)
        {
            WriteLine($"{item.Key} {(string.IsNullOrWhiteSpace(item.DisplayName) ? "<unnamed>" : item.DisplayName)}");
        }
    }

    private void ShowItem(string keyText)
    {
        if (!ItemCatalog.TryGet(keyText, out var item))
        {
            WriteLine($"Item not found: {keyText}");
            return;
        }

        WriteLine($"{item.Key} | {item.DisplayName} | type={item.ItemType} | stack={item.MaxStack}");
        var effects = ItemCatalog.GetEffects(item.Key);
        if (effects.Count == 0)
        {
            WriteLine("Effects: none or runtime effect data is not ready.");
            return;
        }

        foreach (var effect in effects)
        {
            WriteLine(
                $"effect {effect.Key} ({effect.DisplayName}) value={effect.Value} " +
                $"duration={effect.Duration} percent={effect.IsPercent}");
        }
    }

    private void GiveItem(string argumentText)
    {
        var arguments = argumentText.Split(
            ' ',
            2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (arguments.Length == 0 || !ItemKey.TryParse(arguments[0], out var key))
        {
            WriteLine("Usage: item give <registered-key> [count]. Inject the item first.");
            return;
        }

        var count = 1;
        if (arguments.Length > 1 &&
            (!int.TryParse(arguments[1], out count) || count <= 0))
        {
            WriteLine("Count must be a positive integer.");
            return;
        }

        var result = ModApi.Inventory.TryAdd(key, count);
        WriteLine(
            $"Inventory add {key}: status={result.Status}, changed={result.ChangedCount}, " +
            $"remaining={result.RemainingCount}, message={result.Message}");
    }

    private void ExecuteInventoryCommand(string subcommand, string argumentText)
    {
        if (string.IsNullOrWhiteSpace(subcommand))
        {
            ShowInventory();
            return;
        }

        if (subcommand is not ("add" or "remove"))
        {
            WriteLine("Usage: inventory | inventory add <key> [count] | inventory remove <key> [count]");
            return;
        }

        var arguments = argumentText.Split(
            ' ',
            2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (arguments.Length == 0 || !ItemKey.TryParse(arguments[0], out var key))
        {
            WriteLine("A valid namespaced item key is required.");
            return;
        }

        var count = 1;
        if (arguments.Length > 1 &&
            (!int.TryParse(arguments[1], out count) || count <= 0))
        {
            WriteLine("Count must be a positive integer.");
            return;
        }

        var result = subcommand == "add"
            ? ModApi.Inventory.TryAdd(key, count)
            : ModApi.Inventory.TryRemove(key, count);
        WriteLine(
            $"Inventory {subcommand} {key}: status={result.Status}, " +
            $"changed={result.ChangedCount}, remaining={result.RemainingCount}, " +
            $"message={result.Message}");
    }

    private void ShowInventory()
    {
        if (!ModApi.Inventory.TryGetSnapshot(out var inventory) || inventory is null)
        {
            WriteLine("Inventory snapshot is not available.");
            return;
        }

        WriteLine($"Inventory: {inventory.Items.Count} stacks, character={inventory.CharacterId}.");
        foreach (var stack in inventory.Items.Take(40))
        {
            WriteLine(
                $"{stack.ItemKey} x{stack.Count} at {stack.Location.Container} " +
                $"({stack.Location.PlaceId},{stack.Location.SubPlaceId}), fullGrid={stack.IsFullGrid}");
        }
    }

    private void ListMods()
    {
        WriteLine($"Mods: {ModRegistry.DiscoveredMods.Count} discovered.");
        foreach (var mod in ModRegistry.DiscoveredMods)
        {
            WriteLine($"{mod.Manifest.Id} v{mod.Manifest.Version}");
        }
    }

    private void ExecuteRoomCommand(string subcommand, string argumentText)
    {
        subcommand = subcommand.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(subcommand))
        {
            var rooms = ModApi.Rooms.GetRegisteredRooms();
            WriteLine($"Rooms: {rooms.Count} registered.");
            foreach (var room in rooms)
            {
                WriteLine(
                    $"{room.Key} id={room.NativeId} name={room.Name} " +
                    $"source={room.Source} loaded={room.IsLoaded}");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(argumentText))
        {
            WriteLine(
                "Usage: rooms | rooms get <key> | rooms goto <key> | " +
                "rooms unregister <key> | rooms unregister-all <modId>");
            return;
        }

        switch (subcommand)
        {
            case "get":
                if (ModApi.Rooms.TryGet(argumentText, out var room))
                {
                    WriteLine(
                        $"Room {room.Key}: id={room.NativeId}, name={room.Name}, " +
                        $"source={room.Source}, loaded={room.IsLoaded}");
                }
                else
                {
                    WriteLine($"Room not found: {argumentText}");
                }

                break;
            case "goto":
                WriteLine(
                    ModApi.Rooms.GoTo(argumentText)
                        ? $"Room transition requested: {argumentText}"
                        : $"Room transition failed: {argumentText}");
                break;
            case "unregister":
                WriteLine(
                    ModApi.Rooms.Unregister(argumentText)
                        ? $"Room unregistered: {argumentText}"
                        : $"Room unregister failed or not found: {argumentText}");
                break;
            case "unregister-all":
                WriteLine(
                    $"Rooms unregistered for {argumentText}: " +
                    ModApi.Rooms.UnregisterAll(argumentText));
                break;
            default:
                WriteLine("Unknown rooms command. Use rooms without arguments for a list.");
                break;
        }
    }

    private void ShowState()
    {
        if (GameContext.Current is not { } state)
        {
            WriteLine("Game state is not available.");
            return;
        }

        WriteLine(
            $"ready={state.IsGameplayReady} archive={state.ArchiveId} " +
            $"map={state.MapId} ({state.MapName})");
        if (state.Player is { } player)
        {
            WriteLine(
                $"player={player.CharacterId} health={player.Health}/{player.MaxHealth} " +
                $"energy={player.Energy}/{player.MaxEnergy}");
        }
    }

    private void WriteLine(string line)
    {
        _lines.Add(line);
        while (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
        }

        _scrollPosition.y = float.MaxValue;
    }

    private sealed class ConsoleItem : Item
    {
        public ConsoleItem(ItemKey key, string displayName)
            : base(
                key,
                displayName,
                "custom",
                backgroundDescription: "Registered from LoaderConsole.")
        {
        }
    }
}
