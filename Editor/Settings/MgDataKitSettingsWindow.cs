#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    public sealed class MgDataKitSettingsWindow : EditorWindow {
        private static readonly string[] StandardLocationNames = {
            "Editor Directory",
            "Assets Resources",
            "MgDataKit Directory"
        };

        private static readonly string[] CustomLocationNames = {
            "(Custom Location)",
            "Editor Directory",
            "Assets Resources",
            "MgDataKit Directory"
        };

        private Vector2 _scrollPosition;
        private EMgDataKitSettingsLocation _locationSelection = EMgDataKitSettingsLocation.EditorDirectory;

        public static void OpenWindow() {
            var window = GetWindow<MgDataKitSettingsWindow>(false, "MgDataKit Settings");
            window.minSize = new Vector2(520f, 520f);
            window.Show();
            window.Focus();
        }

        private void OnProjectChange() {
            Repaint();
        }

        private void OnGUI() {
            using (EditorGUILayout.ScrollViewScope scroll = new(_scrollPosition, GUILayout.ExpandHeight(true))) {
                _scrollPosition = scroll.scrollPosition;
                DrawSettings();
            }
        }

        private void DrawSettings() {
            EditorGUILayout.LabelField("MgDataKit Settings", EditorStyles.boldLabel);

            if (!MgDataKitSettingsProvider.TryGet(out var settings, out var settingsError)) {
                DrawMissingOrDuplicateSettings(settingsError);
                return;
            }

            DrawAssetLocation(settings);
            DrawProjectSettings(settings);
            DrawUserPreferenceOverrides();
        }

        private void DrawMissingOrDuplicateSettings(string settingsError) {
            EditorGUILayout.HelpBox(settingsError, MessageType.Warning);
            var paths = MgDataKitSettingsProvider.GetAllAssetPaths();
            for (var i = 0; i < paths.Count; i++)
                EditorGUILayout.LabelField(paths[i], EditorStyles.miniLabel);

            if (paths.Count != 0)
                return;

            _locationSelection = (EMgDataKitSettingsLocation)EditorGUILayout.EnumPopup(
                "创建位置", _locationSelection);
            if (!GUILayout.Button("创建 MgDataKitSettings", GUILayout.Height(24)))
                return;

            if (!MgDataKitSettingsProvider.TryCreate(_locationSelection, out _, out var createError)) {
                EditorUtility.DisplayDialog("创建 Settings 失败", createError, "确定");
                return;
            }
        }

        private void DrawAssetLocation(MgDataKitSettings settings) {
            var settingsPath = AssetDatabase.GetAssetPath(settings);
            EditorGUILayout.ObjectField("Asset", settings, typeof(MgDataKitSettings), false);

            var hasKnownLocation = MgDataKitSettingsProvider.TryResolveLocation(settingsPath, out var actualLocation);
            if (!hasKnownLocation)
                EditorGUILayout.HelpBox("Settings 位于自定义路径。", MessageType.Warning);

            DrawStorageLocation(settings, hasKnownLocation, actualLocation);
        }

        private static void DrawStorageLocation(
            MgDataKitSettings settings,
            bool hasKnownLocation,
            EMgDataKitSettingsLocation actualLocation) {
            var currentIndex = hasKnownLocation ? (int)actualLocation : 0;
            string[] options = hasKnownLocation ? StandardLocationNames : CustomLocationNames;
            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup("存储位置", currentIndex, options);
            if (!EditorGUI.EndChangeCheck())
                return;

            var targetIndex = hasKnownLocation ? selectedIndex : selectedIndex - 1;
            if (targetIndex < 0)
                return;

            var targetLocation = (EMgDataKitSettingsLocation)targetIndex;
            if (!MgDataKitSettingsProvider.TryMove(settings, targetLocation, out var moveError)) {
                EditorUtility.DisplayDialog("移动 Settings 失败", moveError, "确定");
                return;
            }

            GUIUtility.ExitGUI();
        }

        private static void DrawProjectSettings(MgDataKitSettings settings) {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("项目默认值", EditorStyles.boldLabel);
            var serialized = new SerializedObject(settings);
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("_autoImportEnabled"), new GUIContent("自动导入"));
            EditorGUILayout.PropertyField(
                serialized.FindProperty("_automaticLintEnabled"), new GUIContent("自动 Lint"));

            if (serialized.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        private static void DrawUserPreferenceOverrides() {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("本机覆盖", EditorStyles.boldLabel);
            var preferences = MgDataKitUserPreferencesStore.Data;

            DrawUserPreferenceOverride(
                "自动导入",
                !preferences.HasAutoImportOverride,
                MgDataKitUserPreferencesStore.GetAutoImportEnabled(),
                MgDataKitUserPreferencesStore.SetAutoImportOverride);
            DrawUserPreferenceOverride(
                "自动 Lint",
                !preferences.HasAutomaticLintOverride,
                MgDataKitUserPreferencesStore.GetAutomaticLintEnabled(),
                MgDataKitUserPreferencesStore.SetAutomaticLintOverride);

            if (!GUILayout.Button("清除本机覆盖"))
                return;

            MgDataKitUserPreferencesStore.SetAutoImportOverride(null);
            MgDataKitUserPreferencesStore.SetAutomaticLintOverride(null);
        }

        private static void DrawUserPreferenceOverride(
            string label,
            bool followsProject,
            bool effectiveValue,
            Action<bool?> setOverride) {
            var newFollowsProject = EditorGUILayout.ToggleLeft($"{label}：使用项目默认", followsProject);
            if (newFollowsProject != followsProject) {
                setOverride(newFollowsProject ? null : effectiveValue);
                followsProject = newFollowsProject;
            }

            EditorGUI.BeginDisabledGroup(followsProject);
            var newValue = EditorGUILayout.ToggleLeft($"{label}：本机值", effectiveValue);
            EditorGUI.EndDisabledGroup();
            if (!followsProject && newValue != effectiveValue)
                setOverride(newValue);
        }
    }
}
#endif
