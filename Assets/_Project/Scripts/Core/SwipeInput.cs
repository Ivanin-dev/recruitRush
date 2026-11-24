using UnityEngine;
using UnityEngine.InputSystem;

namespace RecruitRush
{
    /// <summary>Touch drag input (Pointer covers the touchscreen on device and the mouse in the Editor).</summary>
    public static class SwipeInput
    {
        public static bool Pressed
        {
            get
            {
                var p = Pointer.current;
                return p != null && p.press.isPressed;
            }
        }

        public static bool PressedThisFrame
        {
            get
            {
                var p = Pointer.current;
                return p != null && p.press.wasPressedThisFrame;
            }
        }

        /// <summary>Horizontal drag this frame as a fraction of screen width.</summary>
        public static float DeltaX01
        {
            get
            {
                var p = Pointer.current;
                return p != null && Screen.width > 0 ? p.delta.ReadValue().x / Screen.width : 0f;
            }
        }
    }
}
