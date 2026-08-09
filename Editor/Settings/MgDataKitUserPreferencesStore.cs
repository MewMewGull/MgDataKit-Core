#if UNITY_EDITOR
using System;
using System.IO;
using MgDataKit;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    internal sealed class MgDataKitUserPreferencesData {
        public bool HasAutoImportOverride;
        public bool AutoImportEnabled;
        public bool HasAutomaticLintOverride;
        public bool AutomaticLintEnabled;
        public float LeftPanelWidth = 100f;
    }

    internal static class MgDataKitUserPreferencesStore {
        private const string RelativePath = "UserSettings/MgDataKit.user.json";
        private static MgDataKitUserPreferencesData _data;

        public static MgDataKitUserPreferencesData Data => _data ??= Load();

        public static bool GetAutoImportEnabled() {
            if (Data.HasAutoImportOverride)
                return Data.AutoImportEnabled;
            return MgDataKitSettingsProvider.GetOrNull()?.AutoImportEnabled ?? true;
        }

        public static bool GetAutomaticLintEnabled() {
            if (Data.HasAutomaticLintOverride)
                return Data.AutomaticLintEnabled;
            return MgDataKitSettingsProvider.GetOrNull()?.AutomaticLintEnabled ?? true;
        }

        public static void SetAutoImportOverride(bool? value) {
            Data.HasAutoImportOverride = value.HasValue;
            if (value.HasValue)
                Data.AutoImportEnabled = value.Value;
            Save();
        }

        public static void SetAutomaticLintOverride(bool? value) {
            Data.HasAutomaticLintOverride = value.HasValue;
            if (value.HasValue)
                Data.AutomaticLintEnabled = value.Value;
            Save();
        }

        public static float GetLeftPanelWidth(float fallback) {
            return Data.LeftPanelWidth > 0f ? Data.LeftPanelWidth : fallback;
        }

        public static void SetLeftPanelWidth(float value) {
            Data.LeftPanelWidth = value;
            Save();
        }

        public static void Save() {
            var path = GetAbsolutePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(Data, true));
        }

        private static MgDataKitUserPreferencesData Load() {
            var path = GetAbsolutePath();
            if (!File.Exists(path))
                return new MgDataKitUserPreferencesData();

            try {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<MgDataKitUserPreferencesData>(json) ??
                           new MgDataKitUserPreferencesData();
                return data;
            }
            catch (Exception ex) {
                Debug.LogWarning($"[MgDataKit] 读取本机设置失败，将使用项目默认值：{ex.Message}");
                return new MgDataKitUserPreferencesData();
            }
        }

        private static string GetAbsolutePath() {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativePath));
        }
    }
}
#endif
