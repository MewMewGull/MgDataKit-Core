#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEngine;

namespace MgDataKit.Editor {
    /// <summary>
    /// Excel 专用的行号读取器。其余导入和校验代码只消费来源无关的行引用。
    /// </summary>
    internal sealed class MgDataExcelRowReferenceReader : IMgDataRowReferenceProvider {
        public bool CanHandle(MgDataBase table) {
            return table != null &&
                   string.Equals(MgDataKitAssetCatalogProvider.GetSourceId(table.GetType()), "excel", StringComparison.OrdinalIgnoreCase);
        }

        public List<string> Build(MgDataBase table, int rowCount, string listFieldName) {
            var result = new List<string>(Math.Max(0, rowCount));
            for (var i = 0; i < rowCount; i++)
                result.Add($"asset_row={i + 1}");

            if (table == null || !MgDataKitAssetCatalogProvider.TryGetEntry(
                    table,
                    out MgDataKitAssetEntry entry) ||
                !string.Equals(entry.SourceId, "excel", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.SourceData))
                return result;

            string fullPath = ResolveExcelPath(entry.SourceData);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return result;

            try {
                using FileStream fs = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = ResolveSheet(workbook, listFieldName);
                string rowTypeName = ResolveRowTypeName(table.GetType(), listFieldName);
                if (sheet == null || !TryResolveDataStartRow(sheet, rowTypeName, out int dataStartRow))
                    return result;

                var logical = 0;
                for (var rowIndex = dataStartRow; rowIndex <= sheet.LastRowNum; rowIndex++) {
                    IRow row = sheet.GetRow(rowIndex);
                    if (IsRowEmpty(row))
                        continue;

                    if (logical >= result.Count)
                        break;

                    result[logical] = $"source_row={rowIndex + 1} ({Path.GetFileName(entry.SourceData)})";
                    logical++;
                }
            }
            catch {
                // Excel 读取失败时保留默认 Asset 行号；文件错误由 Excel 导入器/Lint 另行报告。
            }

            return result;
        }

        private static string ResolveExcelPath(string excelPath) {
            if (Path.IsPathRooted(excelPath))
                return excelPath;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", excelPath));
        }

        private static ISheet ResolveSheet(IWorkbook workbook, string listFieldName) {
            if (workbook == null)
                return null;

            var expected = SanitizeSheetName(listFieldName);
            ISheet sheet = string.IsNullOrEmpty(expected) ? null : workbook.GetSheet(expected);
            if (sheet != null)
                return sheet;

            return workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
        }

        private static string SanitizeSheetName(string name) {
            if (string.IsNullOrEmpty(name))
                return "Sheet";

            var chars = new List<char>();
            for (var i = 0; i < name.Length; i++) {
                var c = name[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                    chars.Add(c);
            }

            return chars.Count > 0 ? new string(chars.ToArray()) : "Sheet";
        }

        private static bool TryResolveDataStartRow(ISheet sheet, string rowTypeName, out int dataStartRow) {
            dataStartRow = -1;
            var maxRow = Math.Min(sheet.LastRowNum, 20);
            for (var rowIndex = 0; rowIndex <= maxRow; rowIndex++) {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null)
                    continue;

                var first = row.GetCell(0)?.ToString().Trim();
                if (string.IsNullOrEmpty(first) ||
                    (!string.Equals(first, rowTypeName, StringComparison.Ordinal) &&
                     !MgDataUtils.IsKnownExcelTypeName(first)))
                    continue;

                dataStartRow = rowIndex + 1;
                return true;
            }

            return false;
        }

        private static string ResolveRowTypeName(Type tableType, string listFieldName) {
            if (tableType == null || string.IsNullOrWhiteSpace(listFieldName))
                return null;

            var type = tableType;
            while (type != null && type != typeof(MgDataBase)) {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++) {
                    FieldInfo field = fields[i];
                    if (!string.Equals(field.Name, listFieldName, StringComparison.Ordinal) ||
                        !field.FieldType.IsGenericType ||
                        field.FieldType.GetGenericTypeDefinition() != typeof(List<>))
                        continue;

                    return field.FieldType.GetGenericArguments()[0].Name;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static bool IsRowEmpty(IRow row) {
            if (row == null || row.LastCellNum <= 0)
                return true;

            for (var columnIndex = 0; columnIndex < row.LastCellNum; columnIndex++) {
                ICell cell = row.GetCell(columnIndex);
                if (cell == null || cell.CellType == CellType.Blank)
                    continue;
                if (cell.CellType == CellType.String && string.IsNullOrWhiteSpace(cell.StringCellValue))
                    continue;
                return false;
            }

            return true;
        }
    }
}
#endif
