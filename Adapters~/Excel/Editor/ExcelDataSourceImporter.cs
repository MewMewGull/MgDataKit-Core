#if UNITY_EDITOR

using System;
using System.Reflection;
using MgDataKit;

namespace MgDataKit.Editor {
    /// <summary>
    /// Converts an Excel-bound table into the common grid consumed by Core.
    /// </summary>
    public sealed class ExcelDataSourceImporter : IMgDataSourceImporter {
        public bool CanImport(string sourceId) {
            return string.Equals(sourceId, "excel", StringComparison.OrdinalIgnoreCase);
        }

        public MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry) {
            if (asset == null || entry == null)
                return MgDataSourceReadResult.Failed("Asset 或 Catalog Entry 为空。");
            if (string.IsNullOrWhiteSpace(entry.SourceData))
                return MgDataSourceReadResult.Failed($"Excel 类型缺少有效来源路径：{asset.name}");

            if (!MgDataGridImporter.TryGetListField(asset.GetType(), out FieldInfo listField))
                return MgDataSourceReadResult.Failed("MgData 应有且仅有一个 List<T> 行字段。");

            if (!MgDataUtils.TryReadGridFromExcelPath(
                    asset,
                    listField,
                    entry.SourceData,
                    out string[][] grid,
                    out string errorMessage))
                return MgDataSourceReadResult.Failed(errorMessage);

            return new MgDataSourceReadResult {
                Success = true,
                Grid = grid,
                SourceLabel = entry.SourceData,
                SheetName = SanitizeSheetName(listField.Name)
            };
        }

        private static string SanitizeSheetName(string name) {
            if (string.IsNullOrEmpty(name))
                return "Sheet";

            var builder = new System.Text.StringBuilder();
            foreach (var c in name) {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
                    builder.Append(c);
            }

            return builder.Length > 0 ? builder.ToString() : "Sheet";
        }
    }
}

#endif
