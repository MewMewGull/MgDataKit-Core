using System;
using System.Collections.Generic;
using System.Reflection;

namespace MgDataKit {
    /// <summary>
    /// 主键辅助方法，供数据校验器识别行唯一性。
    /// </summary>
    public static class MgDataImportMergeHelper {
        private const char PrimaryKeySeparator = '\u001f';

        public static FieldInfo[] GetPrimaryKeyFields(Type rowType) {
            if (rowType == null)
                return Array.Empty<FieldInfo>();

            var fields = rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var result = new List<FieldInfo>(fields.Length);
            for (var i = 0; i < fields.Length; i++) {
                if (fields[ i ].GetCustomAttribute<MgDataPrimaryKeyAttribute>() != null)
                    result.Add(fields[ i ]);
            }

            return result.ToArray();
        }

        public static string BuildPrimaryKey(object row, FieldInfo[] pkFields) {
            if (row == null || pkFields == null || pkFields.Length == 0)
                return string.Empty;

            if (pkFields.Length == 1) {
                var value = pkFields[ 0 ].GetValue(row) as string;
                return value?.Trim() ?? string.Empty;
            }

            var parts = new string[ pkFields.Length ];
            for (var i = 0; i < pkFields.Length; i++) {
                var value = pkFields[ i ].GetValue(row) as string;
                parts[ i ] = value?.Trim() ?? string.Empty;
            }

            return string.Join(PrimaryKeySeparator, parts);
        }
    }
}
