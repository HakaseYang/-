using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    /// <summary>
    /// Reads the three controls the game actually uses straight from the devices: a stick
    /// direction, split and spit. No Input Action asset, no callbacks, no asset kept in sync
    /// with the scene - one small class instead of twelve bindings.
    /// </summary>
    public class InputReader
    {
        // the on screen spit button writes here. Static because that button lives in the UI
        // and holds no reference to this reader.
        private static bool mobileSpitHeld;

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
            // a paused or disabled player must stop spitting, whatever the button says
            mobileSpitHeld = false;
            splitWasDown = false;
        }

        /// <summary>WASD, arrow keys or the gamepad stick, clamped to length 1.</summary>
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

        /// <summary>True only on the frame the split key goes down.</summary>
        public bool SplitPressedThisFrame()
        {
            bool down = IsSplitDown();
            bool pressed = down && !splitWasDown;
            splitWasDown = down;
            return pressed;
        }

        /// <summary>Spit repeats while held, so keyboard, gamepad and the phone button all feed
        /// the same TrySpit() call from Update().</summary>
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
