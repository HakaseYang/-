using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QiuQiu
{
    /// <summary>
    /// A UI button that reports "held" instead of "clicked".
    ///
    /// Button.onClick only fires once, on release, so holding the spit button on a phone
    /// produced exactly one pellet. This component watches the pointer instead and keeps
    /// InputReader's mobile spit state on for as long as a finger is on the button.
    /// Several fingers are tracked, so the joystick and the spit button work together.
    /// </summary>
    [DisallowMultipleComponent]
    public class MobileInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Sending input to the spit channel. Only spit is wired to the mobile state right now.")]
        [SerializeField] private bool isSpitButton = true;

        // pointer ids currently pressing this button
        private readonly HashSet<int> pressing = new HashSet<int>();

        private void OnEnable()
        {
            // a disabled button must never leave the game spitting forever
            Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Clear();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            pressing.Add(eventData.pointerId);

            if (isSpitButton)
            {
                InputReader.SetMobileSpitHeld(true);
            }
            else
            {
                // split is a one shot: queue it for the next Update
                InputReader.RequestSplitTap();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null)
            {
                pressing.Remove(eventData.pointerId);
            }

            if (pressing.Count == 0)
            {
                Release();
            }
        }

        // the finger slid off the button: treat it as released, otherwise it never lets go
        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData != null)
            {
                pressing.Remove(eventData.pointerId);
            }

            if (pressing.Count == 0)
            {
                Release();
            }
        }

        private void Clear()
        {
            bool wasPressing = pressing.Count > 0;
            pressing.Clear();

            if (wasPressing)
            {
                Release();
            }
        }

        private void Release()
        {
            if (isSpitButton)
            {
                InputReader.SetMobileSpitHeld(false);
            }
        }
    }
}
