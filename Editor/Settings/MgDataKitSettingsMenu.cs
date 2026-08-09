#if UNITY_EDITOR
using UnityEditor;

namespace MgDataKit.Editor {
    public static class MgDataKitSettingsMenu {
        [MenuItem(MgDataKitEditorMenu.Data.MgDataKitSettings, false, 200)]
        public static void OpenSettings() {
            MgDataKitSettingsWindow.OpenWindow();
        }
    }
}
#endif
