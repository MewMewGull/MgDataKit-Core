using System.IO;
using UnityEditor;

namespace MgDataKit.Editor {
    /// <summary>
    /// 监听 xlsx 的移动/重命名/删除，自动更新 Catalog 中的 Excel 路径绑定。
    /// </summary>
    public class MgDataExcelPathHook : AssetPostprocessor {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
            for (var i = 0; i < movedAssets.Length; i++)
                UpdateExcelReferences(movedFromAssetPaths[ i ], movedAssets[ i ]);

            foreach (var deleted in deletedAssets)
                UpdateExcelReferences(deleted, null);

            MgDataAutoImportService.TryAutoImportByAssetChanges(importedAssets, movedAssets);
        }

        private static void UpdateExcelReferences(string oldPath, string newPath) {
            if (string.Compare(Path.GetExtension(oldPath), ".xlsx", System.StringComparison.OrdinalIgnoreCase) != 0)
                return;

            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out MgDataKitAssetCatalog catalog,
                    out _))
                return;

            var changed = false;
            for (var typeIndex = 0; typeIndex < catalog.Entries.Count; typeIndex++) {
                MgDataKitAssetTypeEntry typeEntry = catalog.Entries[typeIndex];
                if (typeEntry == null)
                    continue;

                for (var assetIndex = 0; assetIndex < typeEntry.Assets.Count; assetIndex++) {
                    MgDataKitAssetEntry entry = typeEntry.Assets[assetIndex];
                    if (entry == null || !string.Equals(entry.SourceId, "excel", System.StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(entry.SourceData, oldPath, System.StringComparison.Ordinal))
                        continue;

                    Undo.RecordObject(catalog, "Update Excel Path");
                    entry.SourceData = newPath ?? string.Empty;
                    changed = true;
                }
            }

            if (changed)
                MgDataKitAssetCatalogProvider.Save(catalog);
        }
    }
}
