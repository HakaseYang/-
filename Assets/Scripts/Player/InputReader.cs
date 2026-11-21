using UnityEngine;
using UnityEngine.InputSystem;

namespace QiuQiu
{
    // reads the Player action map and merges it with the on screen stick.
    public class InputReader
    {
        private InputActionAsset controls;
        private string actionMapName = "Player";
        private string moveActionName = "Move";
        private string splitActionName = "Split";
        private string spitActionName = "Spit";

        private InputAction moveAction;
        private InputAction splitAction;
        private InputAction spitAction;

        private Vector2 virtualMoveInput;
        private bool listening;

        public event System.Action SplitPressed;
        public event System.Action SpitPressed;

        public void Configure(InputActionAsset asset, string mapName, string moveName, string splitName, string spitName)
        {
            controls = asset;
            actionMapName = mapName;
            moveActionName = moveName;
            splitActionName = splitName;
            spitActionName = spitName;
        }
        public void Resolve()
        {
            if (controls == null)
            {
                Debug.LogError("[InputReader] no Controls Input Action Asset, input is disabled");
                return;
            }

            var map = controls.FindActionMap(actionMapName, false); //if not find return false
            if (map == null)
            {
                Debug.LogError($"[InputReader] Input Action Map '{actionMapName}' does not exist");
                return;
            }

            bool wasListening = listening;

            if (wasListening)
            {
                Unsubscribe();
            }

            moveAction = map.FindAction(moveActionName, false);
            splitAction = map.FindAction(splitActionName, false);
            spitAction = map.FindAction(spitActionName, false);

            if (moveAction == null) Debug.LogError($"[InputReader] Action '{moveActionName}' does not exist");
            if (splitAction == null) Debug.LogError($"[InputReader] Action '{splitActionName}' does not exist");
            if (spitAction == null) Debug.LogError($"[InputReader] Action '{spitActionName}' does not exist");

            if (wasListening)
            {
                listening = false;
                Enable();
            }
        }

        public void Enable()
        {
            if (listening)
            {
                return;
            }

            moveAction?.Enable();
            splitAction?.Enable();
            spitAction?.Enable();

            Subscribe();
            listening = true;
        }

        public void Disable()
        {
            if (!listening)
            {
                virtualMoveInput = Vector2.zero;    // drop the stick cache
                return;
            }

            Unsubscribe();

            moveAction?.Disable();
            splitAction?.Disable();
            spitAction?.Disable();

            virtualMoveInput = Vector2.zero;
            listening = false;
        }

        // keyboard / gamepad and the on screen stick, whichever pushes harder
        public Vector2 ReadMove()
        {   
            //get input( stick or WASD)
            Vector2 pad = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            //Keyboard/ Mobile Joystick choses 1 (see who using)
            return pad.sqrMagnitude >= virtualMoveInput.sqrMagnitude ? pad : virtualMoveInput;
        }

        public void SetVirtualMoveInput(Vector2 value)
        {
            //avoid ��2 speed 
            virtualMoveInput = Vector2.ClampMagnitude(value, 1f);
        }

        private void Subscribe()
        {
            if (splitAction != null)
            {
                splitAction.performed += OnSplitPerformed;
            }
            if (spitAction != null)
            {
                spitAction.performed += OnSpitPerformed;
            }
        }

        private void Unsubscribe()
        {
            if (splitAction != null)
            {
                splitAction.performed -= OnSplitPerformed;
            }
            if (spitAction != null)
            {
                spitAction.performed -= OnSpitPerformed;
            }
        }

        private void OnSplitPerformed(InputAction.CallbackContext context)
        {
            if (SplitPressed != null)
            {
                SplitPressed();
            }
        }

        private void OnSpitPerformed(InputAction.CallbackContext context)
        {
            if (SpitPressed != null)
            {
                SpitPressed();
            }
        }
    }
}
