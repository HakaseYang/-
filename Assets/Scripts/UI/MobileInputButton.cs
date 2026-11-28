using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QiuQiu
{
    [DisallowMultipleComponent]
    public class MobileInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Sending input to the spit channel. Only spit is wired to the mobile state right now.")]
        [SerializeField] private bool isSpitButton = true;

        private readonly HashSet<int> pressing = new HashSet<int>();

        private void OnEnable()
        {
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
