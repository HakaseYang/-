using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    public class InputReader
    {
        private static bool mobileSpitHeld;
        private static bool mobileSplitTap;

        private bool splitWasDown;

        public static void SetMobileSpitHeld(bool held)
        {
            mobileSpitHeld = held;
        }

        public void Enable()
        {
        }

        public void Disable()
        {
            mobileSpitHeld = false;
            mobileSplitTap = false;
            splitWasDown = false;
        }

        public Vector2 ReadMove()
        {
            Vector2 value = Vector2.zero;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value.y -= 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1f;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > value.sqrMagnitude)
                {
                    value = stick;
                }
            }

            return Vector2.ClampMagnitude(value, 1f);
        }

        public bool SplitPressedThisFrame()
        {
            if (mobileSplitTap)
            {
                mobileSplitTap = false;
                splitWasDown = false;
                return true;
            }

            bool down = IsSplitDown();
            bool pressed = down && !splitWasDown;
            splitWasDown = down;
            return pressed;
        }

        public static void RequestSplitTap()
        {
            mobileSplitTap = true;
        }

        public bool SpitHeld
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.kKey.isPressed)
                {
                    return true;
                }

                Gamepad pad = Gamepad.current;
                if (pad != null && pad.buttonWest.isPressed)
                {
                    return true;
                }

                return mobileSpitHeld;
            }
        }

        private static bool IsSplitDown()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.jKey.isPressed || keyboard.spaceKey.isPressed))
            {
                return true;
            }

            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonSouth.isPressed;
        }
    }
}
