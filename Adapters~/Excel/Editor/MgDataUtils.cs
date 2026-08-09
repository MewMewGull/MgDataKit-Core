#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace MgDataKit {
    /// <summary>
    /// MgDataKit Excel 读取与导入工具类
    /// </summary>
    public static class MgDataUtils {
        #region 单元格读取

        /// <summary>
        /// 读取公式单元格的计算结果（而非公式文本）。
        /// 直接读取 Excel 缓存的公式结果，不做运行时公式计算。
        /// </summary>
        private static object GetFormulaResult(ICell cell) {
            if (cell == null) return default;

            // 读取 Excel 缓存的公式结果
            return cell.CachedFormulaResultType switch {
                CellType.Numeric => cell.NumericCellValue,
                CellType.String  => cell.StringCellValue?.Trim(),
                CellType.Boolean => cell.BooleanCellValue,
                _                => string.Empty
            };
        }

        #endregion

        #region Vector 解析

        public static Vector2 ParseVector2(string input) {
            input = input.Trim(' ', '(', ')');
            var parts = input.Split(',');
            if (parts.Length != 2) throw new FormatException("Vector2 格式错误");
            return new Vector2(float.Parse(parts[ 0 ]), float.Parse(parts[ 1 ]));
        }

        public static Vector2Int ParseVector2Int(string input) {
            input = input.Trim(' ', '(', ')');
            var parts = input.Split(',');
            if (parts.Length != 2) throw new FormatException("Vector2Int 格式错误");
            return new Vector2Int(int.Parse(parts[ 0 ]), int.Parse(parts[ 1 ]));
        }

        public static Vector3 ParseVector3(string input) {
            input = input.Trim(' ', '(', ')');
            var parts = input.Split(',');
            if (parts.Length != 3) throw new FormatException("Vector3 格式错误");
            return new Vector3(float.Parse(parts[ 0 ]), float.Parse(parts[ 1 ]), float.Parse(parts[ 2 ]));
        }

        public static Vector3Int ParseVector3Int(string input) {
            input = input.Trim(' ', '(', ')');
            var parts = input.Split(',');
            if (parts.Length != 3) throw new FormatException("Vector3Int 格式错误");
            return new Vector3Int(int.Parse(parts[ 0 ]), int.Parse(parts[ 1 ]), int.Parse(parts[ 2 ]));
        }

        /// <summary>
        /// 将 Excel 中的 ColorHex 字符串解析为 <see cref="Color"/>（基于 <see cref="ColorHex"/> 的 uint RRGGBBAA）。
        /// </summary>
        public static Color ParseColorHex(string input) {
            if (string.IsNullOrWhiteSpace(input))
                return default;
            if (!ColorHex.TryParse(input, out ColorHex hex))
                throw new FormatException($"ColorHex 格式错误: {input}，应为 #RRGGBB 或 #RRGGBBAA（uint RRGGBBAA）");
            return hex;
        }

        #endregion

        /// <summary>
        /// 将 Excel 工作表读取为来源无关的字符串网格。该方法是 Excel 数据源适配器的唯一 NPOI 入口。
        /// </summary>
        public static bool TryReadGridFromExcelPath(
            MgDataBase target,
            FieldInfo listField,
            string excelPath,
            out string[][] grid,
            out string errorMessage) {
            grid = null;
            errorMessage = null;
            if (target == null || listField == null || string.IsNullOrWhiteSpace(excelPath)) {
                errorMessage = "Excel 路径为空。";
                return false;
            }

            var resolvedPath = ResolveExcelPath(excelPath);
            if (!File.Exists(resolvedPath)) {
                errorMessage = $"Excel 文件不存在：{resolvedPath}";
                return false;
            }

            var succeeded = false;
            string[][] readGrid = null;
            string readErrorMessage = null;
            ReadExcelWorkbook(resolvedPath, workbook => {
                ISheet sheet = ResolveListSheet(workbook, listField);
                if (sheet == null) {
                    readErrorMessage = $"Excel 中没有可导入的 Sheet：{resolvedPath}";
                    return;
                }

                readGrid = ReadSheetGrid(sheet);
                succeeded = readGrid != null && readGrid.Length > 0;
                if (!succeeded)
                    readErrorMessage = $"Excel Sheet 为空：{sheet.SheetName}";
            });

            if (!succeeded && string.IsNullOrWhiteSpace(readErrorMessage))
                readErrorMessage = $"无法读取 Excel：{resolvedPath}";
            grid = readGrid;
            errorMessage = readErrorMessage;
            return succeeded;
        }

        /// <summary>Editor 导入器共享的 Addressables 同步加载入口。</summary>
        public static Object LoadAddressableAssetForImport(string key, Type assetType, Type dataType) {
            return LoadAddressableAsset(key, assetType, dataType);
        }

        private static string[][] ReadSheetGrid(ISheet sheet) {
            if (sheet == null || sheet.LastRowNum < 0)
                return Array.Empty<string[]>();

            var rows = new List<string[]>();
            for (var rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++) {
                IRow row = sheet.GetRow(rowIndex);
                var cellCount = row?.LastCellNum ?? 0;
                if (cellCount < 0)
                    cellCount = 0;

                var values = new string[cellCount];
                for (var columnIndex = 0; columnIndex < cellCount; columnIndex++)
                    values[columnIndex] = ReadCellText(row?.GetCell(columnIndex));
                rows.Add(values);
            }

            return rows.ToArray();
        }

        private static string ReadCellText(ICell cell) {
            if (cell == null || cell.CellType == CellType.Blank)
                return string.Empty;

            object value = cell.CellType switch {
                CellType.Numeric => cell.NumericCellValue,
                CellType.String => cell.StringCellValue,
                CellType.Boolean => cell.BooleanCellValue,
                CellType.Formula => GetFormulaResult(cell),
                _ => null
            };
            if (value == null)
                return string.Empty;
            if (value is double doubleValue)
                return doubleValue.ToString("G", CultureInfo.InvariantCulture);
            if (value is float floatValue)
                return floatValue.ToString("G", CultureInfo.InvariantCulture);
            if (value is bool boolValue)
                return boolValue ? "TRUE" : "FALSE";
            return value.ToString().Trim();
        }

        private static string ResolveExcelPath(string path) {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            if (Path.IsPathRooted(path))
                return path;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        private static void ReadExcelWorkbook(string fullPath, Action<IWorkbook> operation) {
            const int maxRetry = 5;
            for (var attempt = 0; attempt < maxRetry; attempt++) {
                try {
                    using FileStream fs = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    XSSFWorkbook workbook = new(fs);
                    operation?.Invoke(workbook);
                    return;
                }
                catch (IOException) when (attempt < maxRetry - 1) {
                    System.Threading.Thread.Sleep(100 * (attempt + 1));
                }
            }
        }

        private static ISheet ResolveListSheet(IWorkbook workbook, FieldInfo listField) {
            if (workbook == null || listField == null)
                return null;

            var sheetName = SanitizeSheetName(listField.Name);
            ISheet sheet = workbook.GetSheet(sheetName);
            if (sheet != null)
                return sheet;

            return workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
        }

        private static string SanitizeSheetName(string name) {
            if (string.IsNullOrEmpty(name)) return "Sheet";
            StringBuilder s = new();
            foreach (var c in name) {
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
                    s.Append(c);
            }
            return s.Length > 0 ? s.ToString() : "Sheet";
        }

        /// <summary>Excel 类型行首格是否为 MgDataKit 可识别类型（基础类型或 Addressable Unity 资源短名）。</summary>
        public static bool IsKnownExcelTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            var trimmed = typeName.Trim();
            if (PrimitiveTypeNameSet.Contains(trimmed))
                return true;

            return ResolveAddressableAssetTypeByShortName(trimmed) != null;
        }

        private static readonly HashSet<string> PrimitiveTypeNameSet = new(StringComparer.OrdinalIgnoreCase) {
            "int", "long", "float", "double", "bool", "string", "Vector2", "Vector3", "Vector2Int", "Vector3Int", "Color", "ColorHex", "enum", "List<int>", "List<float>", "List<string>"
        };

        /// <summary>通过 Addressable Key 同步加载资源（仅 Editor 导入时使用）</summary>
        private static Object LoadAddressableAsset(string key, Type assetType, Type dataType) {
            if (assetType == null || !IsAddressableAssetType(assetType))
                return null;

            MethodInfo typedLoader = LoadAddressableAssetTypedMethod;
            if (typedLoader == null) {
                Debug.LogError("[MgDataKit] 无法反射 LoadAddressableAssetTyped");
                return null;
            }

            try {
                MethodInfo closed = typedLoader.MakeGenericMethod(assetType);
                return closed.Invoke(null, new object[] { key, dataType }) as Object;
            }
            catch (Exception ex) {
                LogAddressableLoadFailure(dataType, key?.Trim() ?? string.Empty, ex.InnerException?.Message ?? ex.Message);
                return null;
            }
        }

        private static MethodInfo _loadAddressableAssetTypedMethod;

        private static MethodInfo LoadAddressableAssetTypedMethod =>
            _loadAddressableAssetTypedMethod ??= typeof(MgDataUtils).GetMethod(
                nameof(LoadAddressableAssetTyped),
                BindingFlags.NonPublic | BindingFlags.Static);

        private static T LoadAddressableAssetTyped<T>(string key, Type dataType) where T : Object {
            if (string.IsNullOrWhiteSpace(key)) return null;

            var trimmedKey = key.Trim();
            if (trimmedKey != key) {
                var tableTypeName = dataType?.Name ?? "UnknownTable";
                Debug.LogWarning(
                    $"[MgDataKit] 数据问题：Addressable Key 含首尾空白，已 Trim 后加载。请修正来源表格单元格。TableType={tableTypeName}, Key=\"{key}\"");
            }

            AsyncOperationHandle<T> handle;
            try {
                handle = Addressables.LoadAssetAsync<T>(trimmedKey);
                T asset = handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Failed) {
                    LogAddressableLoadFailure(dataType, trimmedKey, handle.OperationException?.Message);
                    return null;
                }

                return asset;
            }
            catch (InvalidKeyException ex) {
                LogAddressableLoadFailure(dataType, trimmedKey, ex.Message);
                return null;
            }
        }

        private static void LogAddressableLoadFailure(Type dataType, string key, string error) {
            var tableTypeName = dataType?.Name ?? "UnknownTable";
            Debug.LogWarning(
                $"[MgDataKit] Addressables 加载失败（数据问题：请检查来源表格/Addressable 地址是否与资源一致）。TableType={tableTypeName}, Key={key}, Error={error}");
        }

        private static bool IsAddressableAssetType(Type t) {
            if (t == null)
                return false;
            if (!typeof(Object).IsAssignableFrom(t))
                return false;
            if (t == typeof(Object))
                return false;
            if (typeof(MgDataBase).IsAssignableFrom(t))
                return false;
            if (typeof(Component).IsAssignableFrom(t))
                return false;
            return true;
        }

        private static readonly Dictionary<string, Type> AddressableTypeByShortNameCache = new(StringComparer.OrdinalIgnoreCase);

        private static Type ResolveAddressableAssetTypeByShortName(string shortName) {
            if (string.IsNullOrWhiteSpace(shortName))
                return null;

            if (AddressableTypeByShortNameCache.TryGetValue(shortName, out Type cached))
                return cached;

            Type found = null;
            var ambiguous = false;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Type[] types;
                try {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex) {
                    types = ex.Types.Where(x => x != null).ToArray();
                }

                for (var i = 0; i < types.Length; i++) {
                    Type candidate = types[ i ];
                    if (candidate == null || !string.Equals(candidate.Name, shortName, StringComparison.Ordinal))
                        continue;
                    if (!IsAddressableAssetType(candidate))
                        continue;
                    if (found != null && found != candidate) {
                        ambiguous = true;
                        found = null;
                        break;
                    }

                    found = candidate;
                }

                if (ambiguous)
                    break;
            }

            if (ambiguous) {
                Debug.LogWarning(
                    $"[MgDataKit] Excel 类型名「{shortName}」对应多个 Unity 资源类型，表头识别可能不准确");
            }

            AddressableTypeByShortNameCache[ shortName ] = found;
            return found;
        }

    }
}

#endif
