#if UNITY_EDITOR

using System;
using System.IO;
using MgDataKit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    public sealed class ExcelDataSourceAdapter : IMgDataSourceAdapter, IMgDataSourceAutoImportAdapter {
        public string SourceId => "excel";
        public string DisplayName => "Excel";

        public bool CanHandle(MgDataKitAssetTypeEntry typeEntry) {
            return typeEntry != null && string.Equals(typeEntry.SourceId, SourceId, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryValidate(MgDataKitAssetEntry entry, out string errorMessage) {
            errorMessage = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.SourceData)) {
                errorMessage = "Excel 类型缺少有效来源路径。";
                return false;
            }

            string path = entry.SourceData;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Application.dataPath, "..", path);
            if (!File.Exists(path)) {
                errorMessage = $"Excel 来源路径指向的文件不存在：{entry.SourceData}";
                return false;
            }

            return true;
        }

        public MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry) {
            return new ExcelDataSourceImporter().Read(asset, entry);
        }

        public bool TryInitializeBinding(MgDataKitAssetEntry entry, out string errorMessage) {
            errorMessage = null;
            return entry != null;
        }

        public void BuildBindingUI(MgDataSourceAdapterContext context, VisualElement container) {
            container.AddToClassList("mg-data-kit-source-row");
            ObjectField excelField = new ObjectField("Excel") {
                name = "mg-data-kit-excel-field",
                objectType = typeof(DefaultAsset),
                allowSceneObjects = false
            };
            excelField.RegisterValueChangedCallback(evt => {
                MgDataKitAssetEntry entry = context.Entry;
                if (entry?.Asset == null || context.Editor?.Catalog == null)
                    return;

                Undo.RecordObject(context.Editor.Catalog, "绑定 Excel");
                entry.SourceData = evt.newValue != null
                    ? AssetDatabase.GetAssetPath(evt.newValue)
                    : string.Empty;
                entry.SourceId = SourceId;
                MgDataKitAssetCatalogProvider.Save(context.Editor.Catalog);
                context.Commands?.RequestRefresh(EditorRefreshReason.CatalogChanged);
            });
            container.Add(excelField);
        }

        public void BindBindingUI(MgDataSourceAdapterContext context, VisualElement container) {
            ObjectField excelField = container.Q<ObjectField>("mg-data-kit-excel-field");
            if (excelField == null)
                return;

            DefaultAsset excel = string.IsNullOrWhiteSpace(context.Entry?.SourceData)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(context.Entry.SourceData);
            excelField.SetValueWithoutNotify(excel);
        }

        public bool TryOpenSource(MgDataKitAssetEntry entry, out string errorMessage) {
            errorMessage = null;
            if (!TryValidate(entry, out errorMessage))
                return false;

            string path = entry.SourceData;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Application.dataPath, "..", path);
            EditorUtility.OpenWithDefaultApp(Path.GetFullPath(path));
            return true;
        }

        public bool CanHandleAssetChange(string path) {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryGetSourcePath(MgDataKitAssetEntry entry, out string fullPath) {
            fullPath = null;
            if (entry == null || !string.Equals(entry.SourceId, SourceId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.SourceData))
                return false;

            fullPath = Path.IsPathRooted(entry.SourceData)
                ? entry.SourceData
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", entry.SourceData));
            return File.Exists(fullPath);
        }
    }
}

#endif
