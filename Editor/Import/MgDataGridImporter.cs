#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MgDataKit;
using UnityEngine;

namespace MgDataKit.Editor {
    /// <summary>
    /// 将不同数据源读取出的二维文本网格映射为 MgData 行对象。
    /// </summary>
    public static class MgDataGridImporter {
        public static bool TryGetListField(Type assetType, out FieldInfo listField) {
            return TryGetSingleListField(assetType, out listField);
        }

        internal static List<object> SnapshotRows(MgDataBase target) {
            if (target == null || !TryGetSingleListField(target.GetType(), out FieldInfo listField))
                return null;

            var snapshot = new List<object>();
            if (listField.GetValue(target) is not IList list)
                return snapshot;

            for (var i = 0; i < list.Count; i++)
                snapshot.Add(list[i]);
            return snapshot;
        }

        internal static void RestoreRows(MgDataBase target, IReadOnlyList<object> rows) {
            if (target == null || !TryGetSingleListField(target.GetType(), out FieldInfo listField))
                return;

            IList list = listField.GetValue(target) as IList;
            if (list == null) {
                list = (IList)Activator.CreateInstance(listField.FieldType);
                listField.SetValue(target, list);
            }

            list.Clear();
            if (rows == null)
                return;

            for (var i = 0; i < rows.Count; i++)
                list.Add(rows[i]);
        }

        public static bool TryImport(
            MgDataBase target,
            IReadOnlyList<string[]> grid,
            out string errorMessage) {
            errorMessage = null;
            if (target == null) {
                errorMessage = "Asset 为空。";
                return false;
            }

            if (!TryGetSingleListField(target.GetType(), out FieldInfo listField)) {
                errorMessage = "MgData 应有且仅有一个 List<T> 行字段。";
                return false;
            }

            if (!IsListType(listField.FieldType, out Type elemType)) {
                errorMessage = "MgData 行字段不是受支持的 List<T>。";
                return false;
            }

            if (!TryReadParsedRows(
                    target,
                    listField,
                    grid,
                    elemType,
                    out List<object> importedRows,
                    out errorMessage))
                return false;

            IList list = listField.GetValue(target) as IList;
            if (list == null) {
                list = (IList)Activator.CreateInstance(listField.FieldType);
                listField.SetValue(target, list);
            }

            list.Clear();
            for (var i = 0; i < importedRows.Count; i++)
                list.Add(importedRows[i]);

            return true;
        }

        public static void Import(MgDataBase target, IReadOnlyList<string[]> grid) {
            TryImport(target, grid, out _);
        }

        private static bool TryReadParsedRows(
            MgDataBase target,
            FieldInfo listField,
            IReadOnlyList<string[]> grid,
            Type elemType,
            out List<object> rows,
            out string errorMessage) {
            rows = new List<object>();
            errorMessage = null;
            if (target == null || listField == null || grid == null || elemType == null)
                return SetReadError(out errorMessage, "导入网格或行类型为空。", rows);

            List<ColumnInfo> columns = BuildColumnInfos(elemType);
            if (columns.Count == 0)
                return SetReadError(out errorMessage, $"行类型 {elemType.Name} 没有可导入字段。", rows);

            var header = ParseHeaderStructure(grid, columns);
            if (header.HeaderRowCount <= 0 || header.Columns == null || header.Columns.Count == 0)
                return SetReadError(out errorMessage, "未找到有效的三行表头或可匹配字段。", rows);

            for (var rowIndex = header.HeaderRowCount; rowIndex < grid.Count; rowIndex++) {
                string[] dataRow = grid[rowIndex];
                if (IsDataRowEmpty(dataRow))
                    continue;

                object row = Activator.CreateInstance(elemType);
                foreach (KeyValuePair<int, (string[] pathParts, FieldInfo leafField)> column in header.Columns) {
                    string raw = GetCell(dataRow, column.Key);
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    try {
                        object value = ConvertCellValue(raw, column.Value.leafField.FieldType, elemType);
                        if (value != null)
                            SetNestedValue(row, column.Value.pathParts, column.Value.leafField, value);
                    }
                    catch (Exception ex) {
                        Debug.LogWarning(
                            $"[MgDataKit] 数据列解析失败：Table={target.GetType().Name}, " +
                            $"Row={rowIndex + 1}, Field={column.Value.leafField.Name}, " +
                            $"Value={raw}, Error={ex.Message}");
                    }
                }

                rows.Add(row);
                target.OnImportedRow(row);
            }

            return true;
        }

        private static bool SetReadError(out string errorMessage, string message, List<object> rows) {
            errorMessage = message;
            rows.Clear();
            return false;
        }

        private sealed class ColumnInfo {
            public string[] PathParts;
            public FieldInfo LeafField;
        }

        private static List<ColumnInfo> BuildColumnInfos(Type elemType) {
            var result = new List<ColumnInfo>();
            BuildColumnInfosRec(elemType, new List<string>(), result);
            return result;
        }

        private static void BuildColumnInfosRec(
            Type type,
            List<string> pathParts,
            List<ColumnInfo> result) {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (var i = 0; i < fields.Length; i++) {
                FieldInfo field = fields[i];
                if (field.IsStatic)
                    continue;

                var nextPath = new List<string>(pathParts) { field.Name };
                if (IsSimpleOrUnityType(field.FieldType) || IsSupportedListType(field.FieldType) ||
                    MgDataKitExtensionRegistry.CanConvertValue(field.FieldType)) {
                    result.Add(new ColumnInfo {
                        PathParts = nextPath.ToArray(),
                        LeafField = field
                    });
                    continue;
                }

                if (field.FieldType.IsClass && !field.FieldType.IsAbstract &&
                    !IsListType(field.FieldType, out _))
                    BuildColumnInfosRec(field.FieldType, nextPath, result);
            }
        }

        private static (int HeaderRowCount, Dictionary<int, (string[] pathParts, FieldInfo leafField)> Columns)
            ParseHeaderStructure(IReadOnlyList<string[]> grid, List<ColumnInfo> columns) {
            if (grid == null || grid.Count < 2)
                return (0, null);

            var typeRowIndex = -1;
            var maxScan = Math.Min(grid.Count - 1, 20);
            for (var rowIndex = 0; rowIndex <= maxScan; rowIndex++) {
                string firstCell = GetCell(grid[rowIndex], 0);
                if (!string.IsNullOrEmpty(firstCell) &&
                    MgDataKitExtensionRegistry.IsKnownValueTypeName(firstCell.Trim())) {
                    typeRowIndex = rowIndex;
                    break;
                }
            }

            if (typeRowIndex < 1) {
                int variableNameRowIndex = FindVariableNameRowIndex(grid, columns);
                typeRowIndex = variableNameRowIndex >= 0 ? variableNameRowIndex + 1 : 1;
            }

            if (grid.Count <= typeRowIndex)
                return (0, null);

            var columnMap = new Dictionary<int, (string[] pathParts, FieldInfo leafField)>();
            var usedColumns = new HashSet<ColumnInfo>();
            string[] variableNameRow = grid[typeRowIndex - 1];
            if (variableNameRow == null)
                return (0, null);

            for (var columnIndex = 0; columnIndex < variableNameRow.Length; columnIndex++) {
                string name = variableNameRow[columnIndex]?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                ColumnInfo match = columns.Find(column =>
                    !usedColumns.Contains(column) &&
                    string.Equals(
                        column.PathParts[column.PathParts.Length - 1],
                        name,
                        StringComparison.Ordinal));
                if (match == null)
                    continue;

                columnMap[columnIndex] = (match.PathParts, match.LeafField);
                usedColumns.Add(match);
            }

            return (typeRowIndex + 1, columnMap);
        }

        private static int FindVariableNameRowIndex(
            IReadOnlyList<string[]> grid,
            List<ColumnInfo> columns) {
            var bestRowIndex = -1;
            var bestMatchCount = 0;
            var maxScan = Math.Min(grid.Count - 1, 20);
            for (var rowIndex = 0; rowIndex <= maxScan; rowIndex++) {
                string[] row = grid[rowIndex];
                if (row == null)
                    continue;

                var matchCount = 0;
                for (var columnIndex = 0; columnIndex < row.Length; columnIndex++) {
                    string name = row[columnIndex]?.Trim();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (columns.Exists(column => string.Equals(
                            column.PathParts[column.PathParts.Length - 1],
                            name,
                            StringComparison.Ordinal)))
                        matchCount++;
                }

                if (matchCount > bestMatchCount) {
                    bestMatchCount = matchCount;
                    bestRowIndex = rowIndex;
                }
            }

            return bestRowIndex;
        }

        private static object ConvertCellValue(string raw, Type targetType, Type rowType) {
            if (targetType == typeof(string))
                return raw.Trim();
            if (targetType == typeof(bool)) {
                if (bool.TryParse(raw.Trim(), out var boolValue))
                    return boolValue;
                if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var boolNumber))
                    return boolNumber != 0;
                throw new FormatException($"Boolean 格式错误: {raw}");
            }
            if (targetType == typeof(int))
                return int.Parse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(long))
                return long.Parse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return float.Parse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return double.Parse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (targetType == typeof(decimal))
                return decimal.Parse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
            if (targetType == typeof(Vector2))
                return MgDataValueParser.ParseVector2(raw);
            if (targetType == typeof(Vector3))
                return MgDataValueParser.ParseVector3(raw);
            if (targetType == typeof(Vector2Int))
                return MgDataValueParser.ParseVector2Int(raw);
            if (targetType == typeof(Vector3Int))
                return MgDataValueParser.ParseVector3Int(raw);
            if (targetType == typeof(Color))
                return MgDataValueParser.ParseColor(raw);
            if (targetType == typeof(ColorHex)) {
                if (!ColorHex.TryParse(raw, out ColorHex color))
                    throw new FormatException($"ColorHex 格式错误: {raw}");
                return color;
            }
            if (targetType == typeof(List<int>))
                return ParseListInt(raw);
            if (targetType == typeof(List<float>))
                return ParseListFloat(raw);
            if (targetType == typeof(List<string>))
                return ParseListString(raw);
            if (targetType.IsEnum)
                return ParseEnum(raw, targetType);

            if (MgDataKitExtensionRegistry.TryConvertValue(
                    raw,
                    targetType,
                    rowType,
                    out object converted,
                    out string conversionError))
                return converted;

            if (!string.IsNullOrWhiteSpace(conversionError))
                throw new FormatException(conversionError);

            return Convert.ChangeType(raw.Trim(), targetType, CultureInfo.InvariantCulture);
        }

        private static object ParseEnum(string raw, Type targetType) {
            string normalized = raw.Trim().Trim('\'', '"');
            var dotIndex = normalized.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < normalized.Length - 1)
                normalized = normalized.Substring(dotIndex + 1);

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                return Enum.ToObject(targetType, numeric);
            if (Enum.TryParse(targetType, normalized, true, out var parsed))
                return parsed;

            var compact = CompactEnumToken(normalized);
            foreach (string name in Enum.GetNames(targetType)) {
                if (string.Equals(CompactEnumToken(name), compact, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(targetType, name, true);
            }

            Debug.LogWarning($"[MgDataKit] 枚举解析失败: type={targetType.Name}, raw='{raw}'");
            return Enum.ToObject(targetType, 0);
        }

        private static string CompactEnumToken(string value) {
            var chars = value?.Where(char.IsLetterOrDigit).ToArray() ?? Array.Empty<char>();
            return new string(chars);
        }

        private static List<int> ParseListInt(string raw) {
            var result = new List<int>();
            foreach (string part in raw.Split(',')) {
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<float> ParseListFloat(string raw) {
            var result = new List<float>();
            foreach (string part in raw.Split(',')) {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    result.Add(value);
            }

            return result;
        }

        private static List<string> ParseListString(string raw) {
            var result = new List<string>();
            foreach (string part in raw.Split(';')) {
                if (!string.IsNullOrWhiteSpace(part))
                    result.Add(part.Trim());
            }

            return result;
        }

        private static void SetNestedValue(object root, string[] pathParts, FieldInfo leafField, object value) {
            object current = root;
            for (var i = 0; i < pathParts.Length - 1; i++) {
                FieldInfo field = current.GetType().GetField(
                    pathParts[i],
                    BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                    return;

                object next = field.GetValue(current);
                if (next == null) {
                    next = Activator.CreateInstance(field.FieldType);
                    field.SetValue(current, next);
                }

                current = next;
            }

            leafField.SetValue(current, value);
        }

        private static bool IsDataRowEmpty(string[] row) {
            if (row == null || row.Length == 0)
                return true;

            for (var i = 0; i < row.Length; i++) {
                if (!string.IsNullOrWhiteSpace(row[i]))
                    return false;
            }

            return true;
        }

        private static string GetCell(string[] row, int index) {
            return row != null && index >= 0 && index < row.Length ? row[index] ?? string.Empty : string.Empty;
        }

        private static bool IsListType(Type type, out Type elementType) {
            elementType = null;
            if (type == null || !type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>))
                return false;

            elementType = type.GetGenericArguments()[0];
            return elementType != null;
        }

        private static bool IsSupportedListType(Type type) {
            if (!IsListType(type, out Type elementType))
                return false;

            return elementType == typeof(int) || elementType == typeof(float) || elementType == typeof(string);
        }

        private static bool IsSimpleOrUnityType(Type type) {
            if (type == null)
                return false;
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return true;
            if (type == typeof(Vector2) || type == typeof(Vector3) ||
                type == typeof(Vector2Int) || type == typeof(Vector3Int) ||
                type == typeof(Color) || type == typeof(ColorHex))
                return true;
            return type.IsEnum;
        }

        private static bool TryGetSingleListField(Type type, out FieldInfo listField) {
            listField = null;
            FieldInfo found = null;
            while (type != null && type != typeof(MgDataBase)) {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++) {
                    FieldInfo field = fields[i];
                    if (field.Name == "m_Script")
                        continue;
                    if (!IsListType(field.FieldType, out _))
                        continue;
                    if (found != null)
                        return false;
                    found = field;
                }

                type = type.BaseType;
            }

            listField = found;
            return listField != null;
        }
    }
}
#endif
