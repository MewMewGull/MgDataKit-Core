using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    internal static class MgDataAutoImportService {
        private static bool _isImporting;
        private static double _lastTriggeredAt;
        private const double MinTriggerIntervalSeconds = 0.25d;

        public static bool GetAutoImportEnabled() {
            return MgDataKitUserPreferencesStore.GetAutoImportEnabled();
        }

        public static void TryAutoImportByAssetChanges(string[] importedAssets, string[] movedAssets) {
            if (_isImporting) {
                return;
            }

            if (!GetAutoImportEnabled()) {
                return;
            }

            if (!ContainsSourceChange(importedAssets) && !ContainsSourceChange(movedAssets)) {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastTriggeredAt < MinTriggerIntervalSeconds) {
                return;
            }

            _lastTriggeredAt = now;
            var updatedTypes = new HashSet<string>(StringComparer.Ordinal);
            var importedCount = ImportAssets(GetAllRegisteredAssets(), true, updatedTypes);
            if (importedCount > 0) {
                var tables = string.Join(", ", updatedTypes.OrderBy(t => t, StringComparer.Ordinal));
                Debug.Log($"[MgDataKit] 自动导入完成: 数量={importedCount}, 表={tables}");
            }
        }

        public static int ImportAssets(IEnumerable<MgDataBase> assets, bool onlyWhenSourceChanged, ISet<string> importedTypeNames = null) {
            // 该服务负责本地文件来源；云端来源由其适配器自行提供批量操作。
            if (_isImporting) {
                return 0;
            }

            var importedCount = 0;
            var cacheChanged = false;
            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();

            _isImporting = true;
            try {
                foreach (MgDataBase asset in assets) {
                    if (asset == null) {
                        continue;
                    }

                    if (!MgDataKitAssetCatalogProvider.TryGetEntry(asset, out MgDataKitAssetEntry entry) ||
                        !MgDataKitAssetCatalogProvider.TryGetTypeEntry(
                            asset.GetType(),
                            out MgDataKitAssetTypeEntry typeEntry)) {
                        continue;
                    }

                    IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(typeEntry);
                    IMgDataSourceAutoImportAdapter autoAdapter = adapter as IMgDataSourceAutoImportAdapter;
                    if (autoAdapter == null)
                        continue;
                    if (!TryGetLocalSourcePath(adapter, entry, out var sourcePath))
                        continue;

                    var pathTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) {
                        [sourcePath] = File.GetLastWriteTimeUtc(sourcePath).Ticks
                    };
                    if (onlyWhenSourceChanged && !IsAnySourceChanged(pathTicks)) {
                        continue;
                    }

                    try {
                        var importStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        if (!MgDataImportService.Import(asset))
                            throw new InvalidOperationException("数据源读取或行映射失败。");
                        importStopwatch.Stop();

                        var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                        saveStopwatch.Stop();

                        foreach (var kv in pathTicks) {
                            MgDataLocalCache.SetTimestamp(kv.Key, kv.Value);
                        }

                        importedCount++;
                        cacheChanged = true;
                        importedTypeNames?.Add(asset.GetType().Name);
                        Debug.Log(
                            $"[MgDataKit][Timing] LocalImport Table={asset.GetType().Name}, Asset={asset.name}, " +
                            $"ImportMs={importStopwatch.ElapsedMilliseconds}, SaveMs={saveStopwatch.ElapsedMilliseconds}, " +
                            $"SourceFile={sourcePath}");
                    }
                    catch (Exception ex) {
                        Debug.LogError($"[MgDataKit] 导入失败: {asset.name}\n{ex}");
                    }
                }
            }
            finally {
                _isImporting = false;
            }

            if (cacheChanged)
                MgDataLocalCache.Save();

            var lintStopwatch = System.Diagnostics.Stopwatch.StartNew();
            MgDataScriptValidationGate.ValidateAllImportedTables();
            lintStopwatch.Stop();
            batchStopwatch.Stop();
            Debug.Log(
                $"[MgDataKit][Timing] LocalImportBatch Imported={importedCount}, " +
                $"LintMs={lintStopwatch.ElapsedMilliseconds}, TotalMs={batchStopwatch.ElapsedMilliseconds}");

            return importedCount;
        }

        public static List<MgDataBase> GetAllRegisteredAssets() {
            var list = new List<MgDataBase>();
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(out var catalog, out _))
                return list;

            for (var typeIndex = 0; typeIndex < catalog.Entries.Count; typeIndex++) {
                MgDataKitAssetTypeEntry typeEntry = catalog.Entries[typeIndex];
                if (typeEntry == null)
                    continue;

                for (var assetIndex = 0; assetIndex < typeEntry.Assets.Count; assetIndex++) {
                    MgDataBase asset = typeEntry.Assets[assetIndex]?.Asset;
                    if (asset != null)
                        list.Add(asset);
                }
            }

            return list;
        }

        private static bool ContainsSourceChange(string[] paths) {
            if (paths == null || paths.Length == 0) {
                return false;
            }

            for (var i = 0; i < paths.Length; i++) {
                IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
                for (var adapterIndex = 0; adapterIndex < adapters.Count; adapterIndex++) {
                    if (adapters[adapterIndex] is IMgDataSourceAutoImportAdapter autoAdapter &&
                        autoAdapter.CanHandleAssetChange(paths[i]))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetLocalSourcePath(IMgDataSourceAdapter adapter, MgDataKitAssetEntry entry, out string fullPath) {
            if (adapter is IMgDataSourceAutoImportAdapter autoAdapter)
                return autoAdapter.TryGetSourcePath(entry, out fullPath);
            fullPath = null;
            return false;
        }

        private static bool IsAnySourceChanged(IReadOnlyDictionary<string, long> pathTicks) {
            foreach (var kv in pathTicks) {
                if (MgDataLocalCache.GetTimestamp(kv.Key) != kv.Value)
                    return true;
            }

            return false;
        }
    }
}
