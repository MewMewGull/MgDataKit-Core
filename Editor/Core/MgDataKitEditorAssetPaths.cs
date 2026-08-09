#if UNITY_EDITOR

using UnityEditor.PackageManager;

namespace MgDataKit.Editor {
    internal static class MgDataKitEditorAssetPaths {
        private const string LegacyRoot = "Assets/MgDataKit";

        public static string Resolve(string relativePath) {
            string normalized = (relativePath ?? string.Empty).Trim().TrimStart('/', '\\');
            PackageInfo package = PackageInfo.FindForAssembly(typeof(MgDataKitEditorAssetPaths).Assembly);
            if (package != null && !string.IsNullOrWhiteSpace(package.assetPath))
                return $"{package.assetPath}/{normalized}";

            return $"{LegacyRoot}/{normalized}";
        }
    }
}

#endif
