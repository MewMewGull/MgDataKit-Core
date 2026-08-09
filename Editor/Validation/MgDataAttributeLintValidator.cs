using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace MgDataKit.Editor {
    internal static class MgDataAttributeLintValidator {
        private readonly struct TableSnapshot {
            public readonly MgDataBase Table;
            public readonly string AssetPath;
            public readonly IList Rows;
            public readonly Type RowType;
            public readonly string ListFieldName;

            public TableSnapshot(MgDataBase table, string assetPath, IList rows, Type rowType, string listFieldName) {
                Table = table;
                AssetPath = assetPath;
                Rows = rows;
                RowType = rowType;
                ListFieldName = listFieldName;
            }
        }

        public static void Validate(List<string> errors, bool includeSourceRowReferences = true) {
            if (errors == null)
                return;

            ValidateDataSourceBindings(errors);

            var snapshots = LoadAllTableSnapshots();
            for (var i = 0; i < snapshots.Count; i++)
                ValidatePrimaryKeyUniqueness(errors, snapshots[ i ], includeSourceRowReferences);
        }

        static void ValidateDataSourceBindings(List<string> errors) {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            for (var i = 0; i < guids.Length; i++) {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MgDataBase table = AssetDatabase.LoadAssetAtPath<MgDataBase>(path);
                if (table == null)
                    continue;

                Type tableType = table.GetType();
                string sourceError = null;
                bool sourceValid = MgDataKitAssetCatalogProvider.TryGetTypeEntry(
                                       tableType,
                                       out MgDataKitAssetTypeEntry typeEntry) &&
                                   MgDataKitAssetCatalogProvider.TryGetEntry(
                                       table,
                                       out MgDataKitAssetEntry entry) &&
                                   MgDataKitAssetCatalogProvider.TryValidateSourceBinding(
                                       entry,
                                       typeEntry,
                                       out sourceError);
                if (!sourceValid) {
                    AddIssue(
                        errors,
                        EMgDataLintSeverity.Error,
                        sourceError ?? "MgData 数据源配置无效",
                        $"table={tableType.Name}, asset={table.name}, path={path}");
                }
            }

        }

        private static void ValidatePrimaryKeyUniqueness(
            List<string> errors,
            TableSnapshot snapshot,
            bool includeSourceRowReferences) {
            var pkFields = MgDataImportMergeHelper.GetPrimaryKeyFields(snapshot.RowType);
            if (pkFields.Length == 0)
                return;

            for (var i = 0; i < pkFields.Length; i++) {
                if (pkFields[ i ].FieldType != typeof(string)) {
                    AddIssue(
                        errors,
                        EMgDataLintSeverity.Error,
                        "主键字段类型无效",
                        $"table={snapshot.Table.GetType().Name}, field={pkFields[ i ].Name}, expected=string");
                }
            }

            var pkLabel = BuildPrimaryKeyLabel(pkFields);
            EMgDataLintSeverity severity = ResolveDuplicateSeverity(pkFields);
            var logicalRowRefs = includeSourceRowReferences
                ? MgDataKitExtensionRegistry.BuildRowReferences(
                    snapshot.Table,
                    snapshot.Rows.Count,
                    snapshot.ListFieldName)
                : BuildAssetRowReferences(snapshot.Rows.Count);
            var rowsByPk = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var duplicatePks = new HashSet<string>(StringComparer.Ordinal);

            for (var rowIndex = 0; rowIndex < snapshot.Rows.Count; rowIndex++) {
                var rowObj = snapshot.Rows[ rowIndex ];
                if (rowObj == null)
                    continue;

                var pk = MgDataImportMergeHelper.BuildPrimaryKey(rowObj, pkFields);
                if (string.IsNullOrEmpty(pk))
                    continue;

                if (!rowsByPk.TryGetValue(pk, out var rowRefs)) {
                    rowRefs = new List<string>();
                    rowsByPk[ pk ] = rowRefs;
                }

                rowRefs.Add(GetLogicalRowReference(logicalRowRefs, rowIndex));
                if (rowRefs.Count > 1)
                    duplicatePks.Add(pk);
            }

            if (duplicatePks.Count == 0)
                return;

            var duplicateValues = string.Join("|", duplicatePks);
            var rowsSummary = BuildDuplicateRowsSummary(rowsByPk, duplicatePks);
            AddIssue(
                errors,
                severity,
                "主键重复",
                $"table={snapshot.Table.GetType().Name}, primaryKey={pkLabel}, values={duplicateValues}, rows={rowsSummary}");
        }

        private static string BuildPrimaryKeyLabel(FieldInfo[] pkFields) {
            if (pkFields.Length == 1)
                return pkFields[ 0 ].Name;

            StringBuilder label = new(pkFields[ 0 ].Name);
            for (var i = 1; i < pkFields.Length; i++) {
                label.Append('+');
                label.Append(pkFields[ i ].Name);
            }

            return label.ToString();
        }

        private static EMgDataLintSeverity ResolveDuplicateSeverity(FieldInfo[] pkFields) {
            EMgDataLintSeverity severity = EMgDataLintSeverity.Error;
            for (var i = 0; i < pkFields.Length; i++) {
                MgDataPrimaryKeyAttribute attr = pkFields[ i ].GetCustomAttribute<MgDataPrimaryKeyAttribute>();
                if (attr == null)
                    continue;

                if (attr.DuplicateSeverity == EMgDataLintSeverity.Error)
                    return EMgDataLintSeverity.Error;
                severity = EMgDataLintSeverity.Warning;
            }

            return severity;
        }

        private static void AddIssue(List<string> errors, EMgDataLintSeverity severity, string summary, string details) {
            var sev = severity == EMgDataLintSeverity.Warning ? "Warning" : "Error";
            errors.Add($"[MgDataKit][ScriptLint][{sev}] {summary} {details}");
        }

        private static string BuildDuplicateRowsSummary(Dictionary<string, List<string>> rowsByPk, HashSet<string> duplicatedPks) {
            var parts = new List<string>();
            foreach (var pk in duplicatedPks) {
                if (!rowsByPk.TryGetValue(pk, out var rows) || rows == null || rows.Count == 0)
                    continue;
                parts.Add($"{pk}@{string.Join("|", rows)}");
            }

            return string.Join(";", parts);
        }

        private static string BuildRowReference(int rowIndex) {
            return $"asset_row={rowIndex + 1}";
        }

        private static string GetLogicalRowReference(List<string> logicalRowRefs, int logicalIndex) {
            if (logicalRowRefs == null || logicalIndex < 0 || logicalIndex >= logicalRowRefs.Count)
                return $"asset_row={logicalIndex + 1}";
            return logicalRowRefs[ logicalIndex ];
        }

        private static List<string> BuildAssetRowReferences(int rowCount) {
            var result = new List<string>(Math.Max(0, rowCount));
            for (var i = 0; i < rowCount; i++)
                result.Add(BuildRowReference(i));
            return result;
        }

        private static List<TableSnapshot> LoadAllTableSnapshots() {
            var result = new List<TableSnapshot>();
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            for (var i = 0; i < guids.Length; i++) {
                var path = AssetDatabase.GUIDToAssetPath(guids[ i ]);
                MgDataBase table = AssetDatabase.LoadAssetAtPath<MgDataBase>(path);
                if (table == null)
                    continue;

                if (!TryGetSingleListField(table.GetType(), out FieldInfo listField))
                    continue;

                if (!TryGetListElementType(listField.FieldType, out Type rowType))
                    continue;

                IList rows = listField.GetValue(table) as IList;
                if (rows == null)
                    continue;

                result.Add(new TableSnapshot(table, path, rows, rowType, listField.Name));
            }

            return result;
        }

        private static bool TryGetSingleListField(Type type, out FieldInfo listField) {
            listField = null;
            FieldInfo found = null;
            while (type != null && type != typeof(MgDataBase)) {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                            BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++) {
                    FieldInfo field = fields[ i ];
                    if (field.Name == "m_Script")
                        continue;
                    if (!TryGetListElementType(field.FieldType, out _))
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

        private static bool TryGetListElementType(Type listType, out Type elementType) {
            elementType = null;
            if (!listType.IsGenericType)
                return false;
            if (listType.GetGenericTypeDefinition() != typeof(List<>))
                return false;
            var args = listType.GetGenericArguments();
            if (args.Length != 1)
                return false;
            elementType = args[ 0 ];
            return true;
        }
    }
}
