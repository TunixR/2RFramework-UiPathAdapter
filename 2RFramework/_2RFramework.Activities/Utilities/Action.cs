using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _2RFramework.Activities.Utilities
{
    /// <summary>
    /// External action types supported by the parser (resolved from string identifiers).
    /// </summary>
    internal enum ActionType
    {
        [EnumMember(Value = "hotkey")]
        Hotkey,
        [EnumMember(Value = "keydown")]
        KeyDown,
        [EnumMember(Value = "keyup")]
        KeyUp,
        [EnumMember(Value = "type")]
        Type,
        [EnumMember(Value = "click")]
        Click,
        [EnumMember(Value = "left_single")]
        LeftClick,
        [EnumMember(Value = "left_double")]
        DoubleClick,
        [EnumMember(Value = "right_single")]
        RightClick,
        [EnumMember(Value = "hover")]
        Hover,
        [EnumMember(Value = "drag")]
        Drag,
        [EnumMember(Value = "select")]
        Select,
        [EnumMember(Value = "scroll")]
        Scroll,
    }

    /// <summary>
    /// Record of a captured synthetic event (keyboard or mouse) when capture is enabled.
    /// </summary>
    internal sealed class CapturedEvent
    {
        /// <summary>Kind of event (keyboard | mouse).</summary>
        public string Kind { get; set; } = "";
        /// <summary>UTC timestamp when captured.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Arbitrary key-value data for the event.</summary>
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public override string ToString() => $"{TimestampUtc:o} {Kind} {{{string.Join(", ", Data)}}}";
    }

    /// <summary>
    /// Provides parsing of action descriptors and simulated input dispatch with optional capture mode.
    /// </summary>
    internal static class Action
    {
        #region Native interop structs

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        #endregion

        #region Native constants

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;

        #endregion

        #region Injection delegates

        /// <summary>Delegate used to send input (override for testing).</summary>
        public static Func<uint, INPUT[], int, uint> SendInputFunc { get; set; } = DefaultSendInput;
        /// <summary>Delegate used to map a character to vk/shift info.</summary>
        public static Func<char, short> VkKeyScanFunc { get; set; } = DefaultVkKeyScan;

        /// <summary>If true, events are captured and NOT sent to the OS.</summary>
        public static bool CaptureEnabled { get; private set; }

        /// <summary>Captured events collected while capture is enabled.</summary>
        public static List<CapturedEvent> CapturedEvents { get; } = new List<CapturedEvent>();

        private static readonly object _captureLock = new object();

        public static void EnableCapture(bool enabled)
        {
            lock (_captureLock)
            {
                CaptureEnabled = enabled;
                if (enabled) CapturedEvents.Clear();
            }
        }

        public static void ClearCaptured()
        {
            lock (_captureLock)
            {
                CapturedEvents.Clear();
            }
        }

        #endregion

        #region Native P/Invoke defaults

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private static uint DefaultSendInput(uint n, INPUT[] arr, int cb) => SendInput(n, arr, cb);
        private static short DefaultVkKeyScan(char c) => VkKeyScan(c);

        #endregion

        #region Parse entrypoint

        public async static Task<bool> Parse(string actionTypeStr, JObject actionInputs)
        {
            ActionType actionType = GetEnumFromString(actionTypeStr);
            switch (actionType)
            {
                case ActionType.Hotkey:
                    return await Hotkey((string)actionInputs["hotkey"]);
                case ActionType.KeyDown:
                    return await KeyDown((string)actionInputs["key"]);
                case ActionType.KeyUp:
                    return await KeyUp((string)actionInputs["key"]);
                case ActionType.Type:
                    return await Type((string)actionInputs["content"]);
                case ActionType.Click:
                    {
                        List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                        float x = Convert.ToSingle(box[0]);
                        float y = Convert.ToSingle(box[1]);
                        return await Click(x, y);
                    }
                case ActionType.LeftClick:
                    {
                        List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                        float x = Convert.ToSingle(box[0]);
                        float y = Convert.ToSingle(box[1]);
                        return await LeftClick(x, y);
                    }
                case ActionType.DoubleClick:
                    {
                        List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                        float x = Convert.ToSingle(box[0]);
                        float y = Convert.ToSingle(box[1]);
                        return await DoubleClick(x, y);
                    }
                case ActionType.RightClick:
                    {
                        List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                        float x = Convert.ToSingle(box[0]);
                        float y = Convert.ToSingle(box[1]);
                        return await RightClick(x, y);
                    }
                case ActionType.Hover:
                    {
                        List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                        float x = Convert.ToSingle(box[0]);
                        float y = Convert.ToSingle(box[1]);
                        return await Hover(x, y);
                    }
                case ActionType.Drag:
                    {
                        List<float> startBox = actionInputs["start_box"].ToObject<List<float>>();
                        List<float> endBox = actionInputs["end_box"].ToObject<List<float>>();
                        float startX = Convert.ToSingle(startBox[0]);
                        float startY = Convert.ToSingle(startBox[1]);
                        float endX = Convert.ToSingle(endBox[0]);
                        float endY = Convert.ToSingle(endBox[1]);
                        return await Drag(startX, startY, endX, endY);
                    }
                case ActionType.Select:
                    {
                        List<float> startBox = actionInputs["start_box"].ToObject<List<float>>();
                        List<float> endBox = actionInputs["end_box"].ToObject<List<float>>();
                        float startX = Convert.ToSingle(startBox[0]);
                        float startY = Convert.ToSingle(startBox[1]);
                        float endX = Convert.ToSingle(endBox[0]);
                        float endY = Convert.ToSingle(endBox[1]);
                        return await Select(startX, startY, endX, endY);
                    }
                case ActionType.Scroll:
                    {
                        string direction = (string)actionInputs["direction"];
                        float x = -1;
                        float y = -1;
                        if (actionInputs.ContainsKey("start_box"))
                        {
                            List<float> box = actionInputs["start_box"].ToObject<List<float>>();
                            x = Convert.ToSingle(box[0]);
                            y = Convert.ToSingle(box[1]);
                        }
                        return await Scroll(direction, x, y);
                    }
                default:
                    return false;
            }
        }

        #endregion

        #region High-level keyboard

        private async static Task<bool> Hotkey(string key)
        {
            var parts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<string> keys = new List<string>(parts);

            foreach (var k in keys)
                if (!await KeyDown(k))
                    return false;

            for (int i = keys.Count - 1; i >= 0; i--)
                if (!await KeyUp(keys[i]))
                    return false;

            return true;
        }

        private async static Task<bool> KeyDown(string key)
        {
            if (!PyAutoGuiToVk.Map.TryGetValue(key, out ushort vk))
                return false;

            return await SendKeyboardVk(vk, false);
        }

        public async static Task<bool> KeyUp(string key)
        {
            if (!PyAutoGuiToVk.Map.TryGetValue(key, out ushort vk))
                return false;

            return await SendKeyboardVk(vk, true);
        }

        private async static Task<bool> Type(string text)
        {
            foreach (char c in text)
                if (!await TypeChar(c))
                    return false;
            return true;
        }

        private async static Task<bool> TypeChar(char c)
        {
            if (!await SendKeyboardVk(c, false, true))  // keyDown
                return false;

            if (!await SendKeyboardVk(c, true, true))   // keyUp
                return false;
            return true;
        }


        private async static Task<bool> SendKeyboardVk(ushort vk, bool keyUp, bool charMode = false)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = charMode ? new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = vk,
                        dwFlags = keyUp ? 0x0004 | 0x0002 : 0x0004u,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    } : new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            if (CaptureEnabled)
            {
                Capture("keyboard", new Dictionary<string, object>
                {
                    { "vk", vk },
                    { "keyUp", keyUp }
                });
                return true;
            }

            return 1 == SendInputFunc(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        #endregion

        #region High-level mouse

        private async static Task<bool> Click(float x, float y) => await LeftClick(x, y);

        private async static Task<bool> LeftClick(float x, float y)
        {
            if (!await Hover(x, y)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_LEFTDOWN)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_LEFTUP)) return false;
            return true;
        }

        private async static Task<bool> DoubleClick(float x, float y) => await LeftClick(x, y) && await LeftClick(x, y);

        private async static Task<bool> RightClick(float x, float y)
        {
            if (!await Hover(x, y)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_RIGHTDOWN)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_RIGHTUP)) return false;
            return true;
        }

        private async static Task<bool> Hover(float x, float y)
        {
            int ax = ToAbsoluteX(x);
            int ay = ToAbsoluteY(y);
            return await SendMouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, ax, ay);
        }

        private async static Task<bool> Drag(float startX, float startY, float endX, float endY)
        {
            if (!await Hover(startX, startY)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_LEFTDOWN)) return false;

            int ax = ToAbsoluteX(endX);
            int ay = ToAbsoluteY(endY);
            if (!await SendMouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, ax, ay)) return false;
            if (!await SendMouseInput(MOUSEEVENTF_LEFTUP)) return false;
            return true;
        }

        private async static Task<bool> Select(float startX, float startY, float endX, float endY) =>
            await Drag(startX, startY, endX, endY);

        private async static Task<bool> Scroll(string direction, float x = -1, float y = -1)
        {
            if (x >= 0 && y >= 0)
                if (!await Hover(x, y))
                    return false;

            int amount = 120;
            direction = direction.ToLowerInvariant();
            return direction switch
            {
                "up" => await SendMouseInput(MOUSEEVENTF_WHEEL, data: amount),
                "down" => await SendMouseInput(MOUSEEVENTF_WHEEL, data: -amount),
                "left" => await SendMouseInput(MOUSEEVENTF_HWHEEL, data: -amount),
                "right" => await SendMouseInput(MOUSEEVENTF_HWHEEL, data: amount),
                _ => false
            };
        }

        #endregion

        #region Low-level send helpers

        private async static Task<bool> SendMouseInput(uint flags, int x = 0, int y = 0, int data = 0)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = x,
                        dy = y,
                        mouseData = (uint)data,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            if (CaptureEnabled)
            {
                Capture("mouse", new Dictionary<string, object>
                {
                    { "flags", flags },
                    { "x", x },
                    { "y", y },
                    { "data", data }
                });
                return true;
            }

            return 1 == SendInputFunc(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private static int ToAbsoluteX(float px)
        {
            return (int)(px * 65535.0f);
        }

        private static int ToAbsoluteY(float py)
        {
            return (int)(py * 65535.0f);
        }

        private static void Capture(string kind, Dictionary<string, object> data)
        {
            lock (_captureLock)
            {
                CapturedEvents.Add(new CapturedEvent
                {
                    Kind = kind,
                    TimestampUtc = DateTime.UtcNow,
                    Data = data
                });
            }
        }

        #endregion

        #region Enum resolution

        private static ActionType GetEnumFromString(string value)
        {
            var type = typeof(ActionType);
            foreach (var field in type.GetFields())
            {
                var attribute = Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) as EnumMemberAttribute;
                if (attribute != null && attribute.Value == value)
                    return (ActionType)field.GetValue(null);
            }
            throw new ArgumentException($"Unknown value: {value}");
        }

        #endregion
    }

    /// <summary>
    /// Mapping from PyAutoGUI-style key names to Windows virtual key codes.
    /// </summary>
    internal static class PyAutoGuiToVk
    {
        public static readonly Dictionary<string, ushort> Map =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Letters
            { "a", 0x41 }, { "b", 0x42 }, { "c", 0x43 }, { "d", 0x44 },
            { "e", 0x45 }, { "f", 0x46 }, { "g", 0x47 }, { "h", 0x48 },
            { "i", 0x49 }, { "j", 0x4A }, { "k", 0x4B }, { "l", 0x4C },
            { "m", 0x4D }, { "n", 0x4E }, { "o", 0x4F }, { "p", 0x50 },
            { "q", 0x51 }, { "r", 0x52 }, { "s", 0x53 }, { "t", 0x54 },
            { "u", 0x55 }, { "v", 0x56 }, { "w", 0x57 }, { "x", 0x58 },
            { "y", 0x59 }, { "z", 0x5A },

            // Number row
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 },
            { "4", 0x34 }, { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 },
            { "8", 0x38 }, { "9", 0x39 },

            // Punctuation / symbols
            { "`", 0xC0 }, { "-", 0xBD }, { "=", 0xBB },
            { "[", 0xDB }, { "]", 0xDD }, { "\\", 0xDC },
            { ";", 0xBA }, { "'", 0xDE }, { ",", 0xBC },
            { ".", 0xBE }, { "/", 0xBF },

            // Space, Enter, Tab
            { "space", 0x20 },
            { " ", 0x20 },
            { "enter", 0x0D },
            { "return", 0x0D },
            { "tab", 0x09 },

            // Backspace, Insert, Delete
            { "backspace", 0x08 },
            { "delete", 0x2E },
            { "del", 0x2E },
            { "insert", 0x2D },
            { "ins", 0x2D },

            // Escape
            { "esc", 0x1B },
            { "escape", 0x1B },

            // Arrow keys
            { "left", 0x25 },
            { "up", 0x26 },
            { "right", 0x27 },
            { "down", 0x28 },

            // Navigation
            { "home", 0x24 },
            { "end", 0x23 },
            { "pageup", 0x21 },
            { "pgup", 0x21 },
            { "pagedown", 0x22 },
            { "pgdn", 0x22 },

            // Function keys
            { "f1", 0x70 }, { "f2", 0x71 }, { "f3", 0x72 },
            { "f4", 0x73 }, { "f5", 0x74 }, { "f6", 0x75 },
            { "f7", 0x76 }, { "f8", 0x77 }, { "f9", 0x78 },
            { "f10", 0x79 }, { "f11", 0x7A }, { "f12", 0x7B },
            { "f13", 0x7C }, { "f14", 0x7D }, { "f15", 0x7E },
            { "f16", 0x7F }, { "f17", 0x80 }, { "f18", 0x81 },
            { "f19", 0x82 }, { "f20", 0x83 }, { "f21", 0x84 },
            { "f22", 0x85 }, { "f23", 0x86 }, { "f24", 0x87 },

            // Modifiers (generic)
            { "shift", 0x10 },
            { "ctrl", 0x11 },
            { "control", 0x11 },
            { "alt", 0x12 },
            { "menu", 0x12 },

            // Left/right modifiers
            { "shiftleft", 0xA0 },
            { "shiftright", 0xA1 },
            { "ctrlleft", 0xA2 },
            { "ctrlright", 0xA3 },
            { "altleft", 0xA4 },
            { "altright", 0xA5 },

            // Windows keys
            { "win", 0x5B },
            { "winleft", 0x5B },
            { "winright", 0x5C },

            // Menu / App key
            { "apps", 0x5D },
            { "application", 0x5D },

            // Lock keys
            { "capslock", 0x14 },
            { "numlock", 0x90 },
            { "scrolllock", 0x91 },

            // Print screen & pause
            { "printscreen", 0x2C },
            { "prtsc", 0x2C },
            { "pause", 0x13 },
            { "break", 0x13 },

            // Numpad numbers
            { "num0", 0x60 }, { "num1", 0x61 }, { "num2", 0x62 },
            { "num3", 0x63 }, { "num4", 0x64 }, { "num5", 0x65 },
            { "num6", 0x66 }, { "num7", 0x67 }, { "num8", 0x68 },
            { "num9", 0x69 },

            // Numpad operators
            { "add", 0x6B },
            { "subtract", 0x6D },
            { "multiply", 0x6A },
            { "divide", 0x6F },
            { "decimal", 0x6E },

            // Browser keys
            { "browserback", 0xA6 },
            { "browserforward", 0xA7 },
            { "browserrefresh", 0xA8 },
            { "browserstop", 0xA9 },
            { "browsersearch", 0xAA },
            { "browserfavorites", 0xAB },
            { "browserhome", 0xAC },

            // Media keys
            { "volumemute", 0xAD },
            { "volumedown", 0xAE },
            { "volumeup", 0xAF },
            { "nexttrack", 0xB0 },
            { "prevtrack", 0xB1 },
            { "stop", 0xB2 },
            { "playpause", 0xB3 },

            // Misc system keys
            { "sleep", 0x5F },
            { "help", 0x2F },
            { "select", 0x29 }, // not the same as ActionType.Select
            { "print", 0x2A },
            { "execute", 0x2B },
            { "clear", 0x0C }
        };
    }
}
