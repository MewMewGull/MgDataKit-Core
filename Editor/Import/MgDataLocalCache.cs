#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    internal sealed class MgDataLocalCacheData {
        public List<MgDataSourceTimestampEntry> SourceTimestamps = new();
    }

    [Serializable]
    internal sealed class MgDataSourceTimestampEntry {
        public string Path;
        public long LastWriteTicksUtc;
    }

    internal static class MgDataLocalCache {
        private const string RelativeCachePath = "Library/MgDataKit/cache.json";
        private static MgDataLocalCacheData _data;

        public static long GetTimestamp(string sourceAbsolutePath) {
            var key = ToCacheKey(sourceAbsolutePath);
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            List<MgDataSourceTimestampEntry> entries = Data.SourceTimestamps;
            for (var i = 0; i < entries.Count; i++) {
                MgDataSourceTimestampEntry entry = entries[i];
                if (string.Equals(entry.Path, key, StringComparison.OrdinalIgnoreCase))
                    return entry.LastWriteTicksUtc;
            }

            return 0;
        }

        public static void SetTimestamp(string sourceAbsolutePath, long ticksUtc) {
            var key = ToCacheKey(sourceAbsolutePath);
            if (string.IsNullOrWhiteSpace(key))
                return;

            List<MgDataSourceTimestampEntry> entries = Data.SourceTimestamps;
            for (var i = 0; i < entries.Count; i++) {
                MgDataSourceTimestampEntry entry = entries[i];
                if (!string.Equals(entry.Path, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.LastWriteTicksUtc = ticksUtc;
                return;
            }

            entries.Add(new MgDataSourceTimestampEntry {
                Path = key,
                LastWriteTicksUtc = ticksUtc
            });
        }

        public static void Save() {
            PruneInvalidEntries(Data);
            var path = GetAbsoluteCachePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(Data, true));
        }

        private static MgDataLocalCacheData Data => _data ??= Load();

        private static MgDataLocalCacheData Load() {
            var path = GetAbsoluteCachePath();
            if (!File.Exists(path))
                return new MgDataLocalCacheData();

            try {
                var data = JsonUtility.FromJson<MgDataLocalCacheData>(File.ReadAllText(path)) ??
                           new MgDataLocalCacheData();
                PruneInvalidEntries(data);
                return data;
            }
            catch (Exception ex) {
                Debug.LogWarning($"[MgDataKit] 读取本地缓存失败，将重新建立：{ex.Message}");
                return new MgDataLocalCacheData();
            }
        }

        private static string ToCacheKey(string path) {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var fullPath = Path.GetFullPath(path);
            var projectRoot = GetProjectRoot();
            var projectPrefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
            return null;
        }

        private static void PruneInvalidEntries(MgDataLocalCacheData data) {
            if (data?.SourceTimestamps == null) {
                if (data != null)
                    data.SourceTimestamps = new List<MgDataSourceTimestampEntry>();
                return;
            }

            var projectRoot = GetProjectRoot();
            var projectPrefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = data.SourceTimestamps.Count - 1; i >= 0; i--) {
                MgDataSourceTimestampEntry entry = data.SourceTimestamps[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || Path.IsPathRooted(entry.Path)) {
                    data.SourceTimestamps.RemoveAt(i);
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, entry.Path));
                if (!fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(fullPath) ||
                    !seenPaths.Add(entry.Path)) {
                    data.SourceTimestamps.RemoveAt(i);
                }
            }
        }

        private static string GetAbsoluteCachePath() {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativeCachePath));
        }

        private static string GetProjectRoot() {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
#endif
