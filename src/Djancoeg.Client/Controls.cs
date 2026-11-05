using System.Globalization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client;

public enum GameAction { Forward, Back, Left, Right, Jump, Crouch, Fire, Ability, Taunt, Camera, Scores, Chat, NextWeapon, PrevWeapon }

/// <summary>
/// Rebindable keyboard/mouse controls plus a fixed gamepad layout. A binding code is a raylib
/// <see cref="KeyboardKey"/> value, or <see cref="MouseBase"/> + <see cref="MouseButton"/> for mouse buttons; 0 = unbound.
/// Arrow keys, C (crouch), right mouse (ability) and the mouse wheel always work as alternates.
/// </summary>
public static class Controls
{
    public const int MouseBase = 1000;
    public const float StickDeadzone = 0.18f;
    public static ClientSettings? Settings { get; set; }

    public static readonly GameAction[] All = Enum.GetValues<GameAction>();

    public static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>
    {
        [nameof(GameAction.Forward)] = (int)KeyboardKey.W,
        [nameof(GameAction.Back)] = (int)KeyboardKey.S,
        [nameof(GameAction.Left)] = (int)KeyboardKey.A,
        [nameof(GameAction.Right)] = (int)KeyboardKey.D,
        [nameof(GameAction.Jump)] = (int)KeyboardKey.Space,
        [nameof(GameAction.Crouch)] = (int)KeyboardKey.LeftControl,
        [nameof(GameAction.Fire)] = MouseBase + (int)MouseButton.Left,
        [nameof(GameAction.Ability)] = (int)KeyboardKey.Q,
        [nameof(GameAction.Taunt)] = (int)KeyboardKey.T,
        [nameof(GameAction.Camera)] = (int)KeyboardKey.V,
        [nameof(GameAction.Scores)] = (int)KeyboardKey.Tab,
        [nameof(GameAction.Chat)] = (int)KeyboardKey.Y,
        [nameof(GameAction.NextWeapon)] = (int)KeyboardKey.E,
        [nameof(GameAction.PrevWeapon)] = 0,
    };

    public static int Binding(GameAction a)
    {
        var map = Settings?.Bindings;
        if (map is not null && map.TryGetValue(a.ToString(), out int code)) return code;
        return Defaults.TryGetValue(a.ToString(), out int d) ? d : 0;
    }

    /// <summary>Binds <paramref name="code"/> to <paramref name="a"/>, unbinding it from any other action.</summary>
    public static void Bind(GameAction a, int code)
    {
        if (Settings is null) return;
        foreach (var k in Settings.Bindings.Keys.ToList()) if (Settings.Bindings[k] == code) Settings.Bindings[k] = 0;
        Settings.Bindings[a.ToString()] = code;
    }

    public static void ResetDefaults()
    {
        if (Settings is not null) Settings.Bindings = new Dictionary<string, int>(Defaults);
    }

    private static bool CodeDown(int code) => code switch
    {
        0 => false,
        >= MouseBase => IsMouseButtonDown((MouseButton)(code - MouseBase)),
        _ => IsKeyDown((KeyboardKey)code),
    };

    private static bool CodePressed(int code) => code switch
    {
        0 => false,
        >= MouseBase => IsMouseButtonPressed((MouseButton)(code - MouseBase)),
        _ => IsKeyPressed((KeyboardKey)code),
    };

    public static bool Down(GameAction a) => CodeDown(Binding(a)) || AltDown(a) || PadDown(a);
    public static bool Pressed(GameAction a) => CodePressed(Binding(a)) || AltPressed(a) || PadPressed(a);

    private static bool AltDown(GameAction a) => a switch
    {
        GameAction.Forward => IsKeyDown(KeyboardKey.Up),
        GameAction.Back => IsKeyDown(KeyboardKey.Down),
        GameAction.Left => IsKeyDown(KeyboardKey.Left),
        GameAction.Right => IsKeyDown(KeyboardKey.Right),
        GameAction.Crouch => IsKeyDown(KeyboardKey.C),
        GameAction.Ability => IsMouseButtonDown(MouseButton.Right),
        _ => false,
    };

    private static bool AltPressed(GameAction a) => a == GameAction.Ability && IsMouseButtonPressed(MouseButton.Right);

    // ------------------------------------------------------------------ gamepad

    public static bool PadAvailable => IsGamepadAvailable(0);

    private static GamepadButton PadButton(GameAction a) => a switch
    {
        GameAction.Jump => GamepadButton.RightFaceDown,
        GameAction.Crouch => GamepadButton.RightFaceRight,
        GameAction.Ability => GamepadButton.RightFaceLeft,
        GameAction.Camera => GamepadButton.RightFaceUp,
        GameAction.Fire => GamepadButton.RightTrigger2,
        GameAction.NextWeapon => GamepadButton.RightTrigger1,
        GameAction.PrevWeapon => GamepadButton.LeftTrigger1,
        GameAction.Scores => GamepadButton.MiddleLeft,
        GameAction.Taunt => GamepadButton.LeftFaceUp,
        _ => GamepadButton.Unknown,
    };

    private static bool PadDown(GameAction a)
    {
        if (!PadAvailable) return false;
        if (a == GameAction.Fire && GetGamepadAxisMovement(0, GamepadAxis.RightTrigger) > 0.3f) return true;
        var b = PadButton(a);
        return b != GamepadButton.Unknown && IsGamepadButtonDown(0, b);
    }

    private static bool PadPressed(GameAction a)
    {
        if (!PadAvailable) return false;
        var b = PadButton(a);
        return b != GamepadButton.Unknown && IsGamepadButtonPressed(0, b);
    }

    public static bool PausePressed => IsKeyPressed(KeyboardKey.Escape) || (PadAvailable && IsGamepadButtonPressed(0, GamepadButton.MiddleRight));

    /// <summary>Left stick movement (x = right, y = forward), deadzoned.</summary>
    public static Vector2 PadMove()
    {
        if (!PadAvailable) return Vector2.Zero;
        return Deadzone(new Vector2(GetGamepadAxisMovement(0, GamepadAxis.LeftX), -GetGamepadAxisMovement(0, GamepadAxis.LeftY)));
    }

    /// <summary>Right stick look (x = right, y = up), deadzoned and curved for precision.</summary>
    public static Vector2 PadLook()
    {
        if (!PadAvailable) return Vector2.Zero;
        var v = Deadzone(new Vector2(GetGamepadAxisMovement(0, GamepadAxis.RightX), -GetGamepadAxisMovement(0, GamepadAxis.RightY)));
        return v * v.Length();
    }

    private static Vector2 Deadzone(Vector2 v)
    {
        float len = v.Length();
        if (len < StickDeadzone) return Vector2.Zero;
        return v / len * MathF.Min(1, (len - StickDeadzone) / (1 - StickDeadzone));
    }

    // ------------------------------------------------------------------ capture & display

    /// <summary>Returns the next pressed key or mouse button as a binding code, or 0. Escape is never captured.</summary>
    public static int CaptureNext()
    {
        int key = GetKeyPressed();
        if (key != 0 && key != (int)KeyboardKey.Escape) return key;
        for (int b = 0; b <= (int)MouseButton.Back; b++)
            if (IsMouseButtonPressed((MouseButton)b)) return MouseBase + b;
        return 0;
    }

    public static string Name(int code)
    {
        if (code == 0) return "-";
        if (code >= MouseBase) return "Mouse " + (code - MouseBase + 1).ToString(CultureInfo.InvariantCulture);
        string n = ((KeyboardKey)code).ToString();
        return n switch
        {
            "LeftControl" => "L-Ctrl", "RightControl" => "R-Ctrl", "LeftShift" => "L-Shift", "RightShift" => "R-Shift",
            "LeftAlt" => "L-Alt", "RightAlt" => "R-Alt",
            "Zero" => "0", "One" => "1", "Two" => "2", "Three" => "3", "Four" => "4", "Five" => "5", "Six" => "6", "Seven" => "7", "Eight" => "8", "Nine" => "9",
            _ when n.StartsWith("Kp", StringComparison.Ordinal) => "Num " + n[2..],
            _ => n,
        };
    }
}
