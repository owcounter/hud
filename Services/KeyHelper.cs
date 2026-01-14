using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Owmeta.Services
{
    public static class KeyHelper
    {
        // Mouse button constants (negative to distinguish from keyboard)
        public const int MOUSE_XBUTTON1 = -1;  // Mouse4 / Back
        public const int MOUSE_XBUTTON2 = -2;  // Mouse5 / Forward

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static string GetKeyName(int keyCode)
        {
            if (keyCode == 0) return "None";
            if (keyCode == MOUSE_XBUTTON1) return "Mouse4";
            if (keyCode == MOUSE_XBUTTON2) return "Mouse5";

            try
            {
                var key = KeyInterop.KeyFromVirtualKey(keyCode);
                return key switch
                {
                    Key.None => "None",
                    Key.F1 => "F1",
                    Key.F2 => "F2",
                    Key.F3 => "F3",
                    Key.F4 => "F4",
                    Key.F5 => "F5",
                    Key.F6 => "F6",
                    Key.F7 => "F7",
                    Key.F8 => "F8",
                    Key.F9 => "F9",
                    Key.F10 => "F10",
                    Key.F11 => "F11",
                    Key.F12 => "F12",
                    Key.OemTilde => "`",
                    Key.OemMinus => "-",
                    Key.OemPlus => "=",
                    Key.OemOpenBrackets => "[",
                    Key.OemCloseBrackets => "]",
                    Key.OemPipe => "\\",
                    Key.OemSemicolon => ";",
                    Key.OemQuotes => "'",
                    Key.OemComma => ",",
                    Key.OemPeriod => ".",
                    Key.OemQuestion => "/",
                    _ => key.ToString()
                };
            }
            catch
            {
                return $"Key {keyCode}";
            }
        }

        public static bool IsMouseButton(int keyCode)
        {
            return keyCode < 0;
        }
    }
}
