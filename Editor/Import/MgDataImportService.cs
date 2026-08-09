#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MgDataKit;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    public static class MgDataImportService {
        public static bool Import(MgDataBase asset) {
            if (asset == null)
                return false;

            if (!MgDataKitAssetCatalogProvider.TryGetTypeEntry(asset.GetType(), out MgDataKitAssetTypeEntry typeEntry)) {
                Debug.LogError(
                    $"[MgDataKit] 无法读取 {asset.GetType().Name} 的 Catalog 类型配置，导入已取消。");
                return false;
            }

            MgDataKitAssetEntry entry = MgDataKitAssetCatalogProvider.RegisterAsset(asset);
            if (entry == null)
                return false;
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(typeEntry);
            if (adapter != null) {
                if (!adapter.TryValidate(entry, out string adapterError)) {
                    Debug.LogError($"[MgDataKit] 数据源绑定校验失败: {asset.name}\n{adapterError}");
                    return false;
                }
            }
            else if (!MgDataKitAssetCatalogProvider.TryValidateSourceBinding(
                         entry,
                         typeEntry,
                         out string sourceError)) {
                Debug.LogError($"[MgDataKit] 数据源绑定校验失败: {asset.name}\n{sourceError}");
                return false;
            }

            IMgDataSourceImporter importer = adapter == null
                ? MgDataKitExtensionRegistry.GetDataSourceImporter(typeEntry.SourceId)
                : null;
            if (adapter == null && importer == null) {
                Debug.LogError(
                    $"[MgDataKit] 未找到数据源 {typeEntry.SourceId} 的导入器，导入已取消。");
                return false;
            }
            MgDataSourceReadResult readResult;
            try {
                readResult = adapter != null
                    ? adapter.Read(asset, entry)
                    : importer.Read(asset, entry);
            }
            catch (Exception ex) {
                Debug.LogError(
                    $"[MgDataKit] {GetSourceName(typeEntry.SourceId)} 读取失败: {asset.name}\n{ex}");
                return false;
            }

            if (readResult == null || !readResult.Success || readResult.Grid == null) {
                string error = readResult?.ErrorMessage ?? "数据源读取失败。";
                Debug.LogError($"[MgDataKit] {GetSourceName(typeEntry.SourceId)} 导入失败: {asset.name}\n{error}");
                return false;
            }

            IReadOnlyList<IMgDataImportExtension> extensions =
                MgDataKitExtensionRegistry.GetImportExtensions(asset);
            var extensionStates = new List<MgDataImportExtensionState>(extensions.Count);
            for (var i = 0; i < extensions.Count; i++) {
                extensionStates.Add(new MgDataImportExtensionState {
                    Extension = extensions[i],
                    Snapshot = extensions[i].CaptureBeforeImport(asset)
                });
            }

            List<object> rowsSnapshot = MgDataGridImporter.SnapshotRows(asset);
            try {
                if (!MgDataGridImporter.TryImport(asset, readResult.Grid, out string mappingError))
                    throw new InvalidDataException(mappingError ?? "未找到有效表头或字段映射。");

                for (var i = 0; i < extensionStates.Count; i++) {
                    MgDataImportExtensionState state = extensionStates[i];
                    if (!state.Extension.TryApply(
                            asset,
                            state.Snapshot,
                            readResult.SourceLabel,
                            out string postProcessError)) {
                        throw new InvalidDataException(
                            postProcessError ?? "导入后处理失败。");
                    }
                }
            }
            catch (Exception ex) {
                MgDataGridImporter.RestoreRows(asset, rowsSnapshot);
                Debug.LogError(
                    $"[MgDataKit] {GetSourceName(typeEntry.SourceId)} 行映射失败: {asset.name}\n{ex}");
                return false;
            }

            EditorUtility.SetDirty(asset);
            return true;
        }

        private static string GetSourceName(string sourceId) {
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(sourceId);
            return adapter?.DisplayName ?? sourceId;
        }

    }

}
#endif
