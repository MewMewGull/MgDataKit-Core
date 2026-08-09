#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MgDataKit;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MgDataKit.Editor {
    internal sealed class DataTableTagManagementWindow : EditorWindow {
        private MgDataKitAssetCatalog _catalog;
        private ReorderableList _tagList;
        private Vector2 _scrollPosition;
        private string _loadError;

        [MenuItem(MgDataKitEditorMenu.Data.MgDataTableTags, false, 1)]
        internal static void Open() {
            DataTableTagManagementWindow window = GetWindow<DataTableTagManagementWindow>(
                false,
                "MgData 全局 Tag 管理");
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        private void OnEnable() {
            Undo.undoRedoPerformed += HandleUndoRedo;
            ReloadCatalog();
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        private void OnGUI() {
            if (_catalog == null) {
                EditorGUILayout.HelpBox(
                    string.IsNullOrWhiteSpace(_loadError) ? "无法加载 MgData Catalog。" : _loadError,
                    MessageType.Warning);
                if (GUILayout.Button("重新加载"))
                    ReloadCatalog();
                return;
            }

            using (EditorGUILayout.ScrollViewScope scroll = new(_scrollPosition)) {
                _scrollPosition = scroll.scrollPosition;
                DrawGlobalTags();
            }
        }

        private void DrawGlobalTags() {
            EditorGUILayout.LabelField("全局 Tag", EditorStyles.boldLabel);
            if (_tagList == null)
                return;

            Rect listRect = GUILayoutUtility.GetRect(
                0f,
                _tagList.GetHeight(),
                GUILayout.ExpandWidth(true));
            _tagList.DoList(listRect);
        }

        private void BuildTagList() {
            _tagList = new ReorderableList(
                _catalog.TagSystem.MutableDefinitions,
                typeof(DataTableTagDefinition),
                true,
                true,
                true,
                true) {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Tag（拖拽调整顺序）"),
                drawElementCallback = DrawTagElement,
                onAddCallback = AddTag,
                onRemoveCallback = RemoveTag,
                onReorderCallbackWithDetails = ReorderTag
            };
        }

        private void DrawTagElement(Rect rect, int index, bool isActive, bool isFocused) {
            List<DataTableTagDefinition> definitions = _catalog.TagSystem.MutableDefinitions;
            if (index < 0 || index >= definitions.Count)
                return;

            DataTableTagDefinition definition = definitions[index];
            if (definition == null)
                return;

            rect.y += 1f;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUI.TextField(rect, definition.Name);
            if (!EditorGUI.EndChangeCheck())
                return;

            string oldName = definition.Name;
            if (!_catalog.TagSystem.TryRename(definition, newName, out string errorMessage)) {
                ShowNotification(new GUIContent(errorMessage));
                return;
            }

            string normalizedName = definition.Name;
            definition.SetName(oldName);
            Undo.RecordObject(_catalog, "重命名 MgData Tag");
            definition.SetName(normalizedName);
            SaveCatalogAndRefresh();
        }

        private void AddTag(ReorderableList list) {
            string tagName = BuildUniqueTagName();
            Undo.RecordObject(_catalog, "新增 MgData Tag");
            DataTableTagDefinition definition = _catalog.TagSystem.Add(tagName, out string errorMessage);
            if (definition == null) {
                ShowNotification(new GUIContent(errorMessage));
                return;
            }

            list.index = _catalog.TagSystem.MutableDefinitions.IndexOf(definition);
            SaveCatalogAndRefresh();
        }

        private void RemoveTag(ReorderableList list) {
            List<DataTableTagDefinition> definitions = _catalog.TagSystem.MutableDefinitions;
            if (list.index < 0 || list.index >= definitions.Count)
                return;

            DataTableTagDefinition definition = definitions[list.index];
            var affectedTableCount = 0;
            for (var i = 0; i < _catalog.Entries.Count; i++) {
                MgDataKitAssetTypeEntry typeEntry = _catalog.Entries[i];
                if (typeEntry != null && typeEntry.Tags.Contains(definition.Id))
                    affectedTableCount++;
            }

            if (!EditorUtility.DisplayDialog(
                    "删除 MgData Tag",
                    $"删除 Tag「{definition.Name}」？\n\n将从 {affectedTableCount} 个表类型中移除此 Tag。",
                    "删除",
                    "取消"))
                return;

            Undo.RecordObject(_catalog, "删除 MgData Tag");
            for (var i = 0; i < _catalog.Entries.Count; i++)
                _catalog.Entries[i]?.Tags.Remove(definition.Id);
            _catalog.TagSystem.Remove(definition);
            list.index = Mathf.Clamp(list.index - 1, -1, definitions.Count - 1);
            SaveCatalogAndRefresh();
        }

        private void ReorderTag(ReorderableList list, int oldIndex, int newIndex) {
            List<DataTableTagDefinition> definitions = _catalog.TagSystem.MutableDefinitions;
            if (oldIndex < 0 || oldIndex >= definitions.Count || newIndex < 0 || newIndex >= definitions.Count)
                return;

            DataTableTagDefinition moved = definitions[newIndex];
            definitions.RemoveAt(newIndex);
            definitions.Insert(oldIndex, moved);
            Undo.RecordObject(_catalog, "调整 MgData Tag 顺序");
            definitions.RemoveAt(oldIndex);
            definitions.Insert(newIndex, moved);
            SaveCatalogAndRefresh();
        }

        private string BuildUniqueTagName() {
            const string baseName = "新 Tag";
            if (_catalog.TagSystem.FindByName(baseName) == null)
                return baseName;

            for (var suffix = 2; suffix < int.MaxValue; suffix++) {
                string candidate = $"{baseName} {suffix}";
                if (_catalog.TagSystem.FindByName(candidate) == null)
                    return candidate;
            }

            return Guid.NewGuid().ToString("N");
        }

        private void ReloadCatalog() {
            _catalog = null;
            _tagList = null;
            _loadError = null;
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out _catalog,
                    out _loadError)) {
                Repaint();
                return;
            }

            BuildTagList();
            Repaint();
        }

        private void SaveCatalogAndRefresh() {
            MgDataKitAssetCatalogProvider.Save(_catalog);
            MgDataKitEditor.RepaintOpenWindows();
            Repaint();
        }

        private void HandleUndoRedo() {
            ReloadCatalog();
            MgDataKitEditor.RepaintOpenWindows();
        }
    }
}
#endif
