#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MgDataKit;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [InitializeOnLoad]
    internal static class MgDataKitSettingsProvider {
        public const string EditorDirectoryPath = "Assets/MgDataKit/Project/MgDataKitSettings.asset";
        public const string AssetsResourcesPath = "Assets/Resources/MgDataKitSettings.asset";
        public const string MgDataKitDirectoryPath = "Assets/MgDataKit/MgDataKitSettings.asset";

        private static MgDataKitSettings _cachedInstance;
        private static List<string> _cachedPaths;

        static MgDataKitSettingsProvider() {
            EditorApplication.projectChanged += InvalidateCache;
        }

        public static MgDataKitSettings GetOrNull() {
            return TryGet(out MgDataKitSettings settings, out _) ? settings : null;
        }

        public static bool TryGet(out MgDataKitSettings settings, out string errorMessage) {
            RefreshCacheIfNeeded();
            settings = null;
            errorMessage = null;

            if (_cachedPaths.Count == 0) {
                errorMessage = "未找到 MgDataKitSettings。";
                return false;
            }

            if (_cachedPaths.Count > 1) {
                errorMessage = "检测到多个 MgDataKitSettings，项目中只能保留一个：\n" +
                               string.Join("\n", _cachedPaths);
                return false;
            }

            if (_cachedInstance == null)
                _cachedInstance = AssetDatabase.LoadAssetAtPath<MgDataKitSettings>(_cachedPaths[0]);

            if (_cachedInstance != null) {
                settings = _cachedInstance;
                return true;
            }

            errorMessage = $"无法加载 MgDataKitSettings：{_cachedPaths[0]}";
            return false;
        }

        public static IReadOnlyList<string> GetAllAssetPaths() {
            RefreshCacheIfNeeded();
            return _cachedPaths;
        }

        public static string GetAssetPath(EMgDataKitSettingsLocation location) {
            return location switch {
                EMgDataKitSettingsLocation.AssetsResources => AssetsResourcesPath,
                EMgDataKitSettingsLocation.MgDataKitDirectory => MgDataKitDirectoryPath,
                _ => EditorDirectoryPath
            };
        }

        public static bool TryResolveLocation(string assetPath, out EMgDataKitSettingsLocation location) {
            if (string.Equals(assetPath, AssetsResourcesPath, StringComparison.OrdinalIgnoreCase)) {
                location = EMgDataKitSettingsLocation.AssetsResources;
                return true;
            }

            if (string.Equals(assetPath, MgDataKitDirectoryPath, StringComparison.OrdinalIgnoreCase)) {
                location = EMgDataKitSettingsLocation.MgDataKitDirectory;
                return true;
            }

            location = EMgDataKitSettingsLocation.EditorDirectory;
            return string.Equals(assetPath, EditorDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryCreate(
            EMgDataKitSettingsLocation location,
            out MgDataKitSettings settings,
            out string errorMessage) {
            settings = null;
            errorMessage = null;
            RefreshCacheIfNeeded();
            if (_cachedPaths.Count > 0) {
                errorMessage = _cachedPaths.Count == 1
                    ? $"项目中已存在 MgDataKitSettings：{_cachedPaths[0]}"
                    : "项目中存在多个 MgDataKitSettings，请先处理重复实例。";
                return false;
            }

            var targetPath = GetAssetPath(location);
            if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null) {
                errorMessage = $"目标位置已有其他资产：{targetPath}";
                return false;
            }

            EnsureAssetFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<MgDataKitSettings>();
            settings.StorageLocation = location;
            AssetDatabase.CreateAsset(settings, targetPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            InvalidateCache();
            TryGet(out settings, out _);
            return settings != null;
        }

        public static bool TryMove(
            MgDataKitSettings settings,
            EMgDataKitSettingsLocation targetLocation,
            out string errorMessage) {
            errorMessage = null;
            if (settings == null) {
                errorMessage = "MgDataKitSettings 为空。";
                return false;
            }

            var currentPath = AssetDatabase.GetAssetPath(settings);
            var targetPath = GetAssetPath(targetLocation);
            if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase)) {
                settings.StorageLocation = targetLocation;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                return true;
            }

            if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null) {
                errorMessage = $"目标位置已有其他资产：{targetPath}";
                return false;
            }

            EnsureAssetFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
            var previousLocation = settings.StorageLocation;
            settings.StorageLocation = targetLocation;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            var moveError = AssetDatabase.MoveAsset(currentPath, targetPath);
            if (!string.IsNullOrEmpty(moveError)) {
                settings.StorageLocation = previousLocation;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                errorMessage = moveError;
                return false;
            }

            AssetDatabase.SaveAssets();
            InvalidateCache();
            return true;
        }

        public static void InvalidateCache() {
            _cachedInstance = null;
            _cachedPaths = null;
        }

        private static void RefreshCacheIfNeeded() {
            if (_cachedPaths != null)
                return;

            _cachedPaths = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:MgDataKitSettings");
            for (var i = 0; i < guids.Length; i++) {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path))
                    _cachedPaths.Add(path);
            }

            _cachedPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureAssetFolder(string folderPath) {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++) {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
