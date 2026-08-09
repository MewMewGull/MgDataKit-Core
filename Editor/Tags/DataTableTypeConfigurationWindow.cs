#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MgDataKit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    /// <summary>
    /// 编辑单个 MgData 表类型的来源和 Tags 配置。
    /// </summary>
    internal sealed class DataTableTypeConfigurationWindow : EditorWindow {
        private const string UssRelativePath =
            "Editor/Tags/DataTableTypeConfigurationWindow.uss";

        private readonly List<Type> _types = new();
        private readonly List<string> _typeNames = new();
        private readonly List<string> _sourceIds = new();
        private readonly List<string> _sourceNames = new();
        private MgDataKitAssetCatalog _catalog;
        private MgDataKitAssetTypeEntry _typeEntry;
        private Type _selectedType;
        private string _loadError;
        private bool _refreshing;

        private Label _titleLabel;
        private Label _summaryLabel;
        private DropdownField _typePopup;
        private ObjectField _scriptField;
        private DropdownField _dataSourceField;
        private ToolbarMenu _tagMenu;
        private Label _tagSummaryLabel;
        private HelpBox _helpBox;

        [MenuItem(MgDataKitEditorMenu.Data.MgDataTableTypeConfiguration, false, 2)]
        private static void OpenFromMenu() {
            Open(null);
        }

        internal static void Open(Type selectedType) {
            DataTableTypeConfigurationWindow window = GetWindow<DataTableTypeConfigurationWindow>(
                false,
                "MgData 表类型配置");
            window.minSize = new Vector2(480f, 360f);
            if (selectedType != null)
                window._selectedType = selectedType;
            window.Show();
            window.RefreshUI();
        }

        internal static void RepaintOpenWindows() {
            DataTableTypeConfigurationWindow[] windows =
                Resources.FindObjectsOfTypeAll<DataTableTypeConfigurationWindow>();
            for (var i = 0; i < windows.Length; i++)
                windows[i].ReloadCatalog();
        }

        private void OnEnable() {
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.projectChanged += ScheduleCatalogReload;
            ReloadCatalog();
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.projectChanged -= ScheduleCatalogReload;
            EditorApplication.delayCall -= ReloadCatalog;
        }

        private void CreateGUI() {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                MgDataKitEditorAssetPaths.Resolve(UssRelativePath));
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            BuildLayout();
            ReloadCatalog();
            RefreshUI();
        }

        private void BuildLayout() {
            rootVisualElement.AddToClassList("mg-data-kit-type-config-root");

            VisualElement heading = new VisualElement { name = "mg-data-kit-type-config-heading" };
            heading.AddToClassList("mg-data-kit-type-config-heading");
            _titleLabel = new Label("MgData 表类型配置");
            _titleLabel.AddToClassList("mg-data-kit-type-config-title");
            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("mg-data-kit-type-config-summary");
            heading.Add(_titleLabel);
            heading.Add(_summaryLabel);
            rootVisualElement.Add(heading);

            _typePopup = new DropdownField("表类型") {
                name = "mg-data-kit-type-config-type-popup"
            };
            _typePopup.RegisterValueChangedCallback(OnTypePopupChanged);
            rootVisualElement.Add(_typePopup);

            _helpBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _helpBox.name = "mg-data-kit-type-config-help";
            _helpBox.style.display = DisplayStyle.None;
            rootVisualElement.Add(_helpBox);

            ScrollView details = new ScrollView(ScrollViewMode.Vertical) {
                name = "mg-data-kit-type-config-details",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            details.AddToClassList("mg-data-kit-type-config-details");

            _scriptField = new ObjectField("MonoScript") {
                name = "mg-data-kit-type-config-script",
                objectType = typeof(MonoScript),
                allowSceneObjects = false
            };
            _scriptField.RegisterValueChangedCallback(OnScriptChanged);
            details.Add(_scriptField);

            _dataSourceField = new DropdownField("数据源") {
                name = "mg-data-kit-type-config-data-source"
            };
            _dataSourceField.RegisterValueChangedCallback(OnDataSourceChanged);
            details.Add(_dataSourceField);

            VisualElement tagsSection = new VisualElement {
                name = "mg-data-kit-type-config-tags-section"
            };
            tagsSection.AddToClassList("mg-data-kit-type-config-tags-section");
            Label tagsTitle = new Label("Tags");
            tagsTitle.AddToClassList("mg-data-kit-type-config-section-title");
            tagsSection.Add(tagsTitle);

            VisualElement tagsRow = new VisualElement {
                name = "mg-data-kit-type-config-tags-row"
            };
            tagsRow.AddToClassList("mg-data-kit-type-config-tags-row");
            _tagMenu = new ToolbarMenu {
                name = "mg-data-kit-type-config-tag-menu",
                text = "选择 Tags",
                tooltip = "多选当前表类型的 Tags"
            };
            _tagMenu.variant = ToolbarMenu.Variant.Popup;
            tagsRow.Add(_tagMenu);
            _tagSummaryLabel = new Label();
            _tagSummaryLabel.AddToClassList("mg-data-kit-type-config-tag-summary");
            tagsRow.Add(_tagSummaryLabel);
            tagsSection.Add(tagsRow);
            details.Add(tagsSection);

            rootVisualElement.Add(details);

            VisualElement actions = new VisualElement {
                name = "mg-data-kit-type-config-actions"
            };
            actions.AddToClassList("mg-data-kit-type-config-actions");
            Button globalTagsButton = new Button(DataTableTagManagementWindow.Open) {
                text = "管理全局 Tag",
                tooltip = "打开全局 Tag 的新增、重命名、删除和排序窗口"
            };
            actions.Add(globalTagsButton);
            rootVisualElement.Add(actions);
        }

        private void OnTypePopupChanged(ChangeEvent<string> evt) {
            if (_refreshing)
                return;

            int index = _typeNames.IndexOf(evt.newValue);
            if (index < 0 || index >= _types.Count)
                return;

            _selectedType = _types[index];
            RefreshUI();
        }

        private void OnScriptChanged(ChangeEvent<UnityEngine.Object> evt) {
            if (_refreshing || _catalog == null || _typeEntry == null)
                return;

            MonoScript newScript = evt.newValue as MonoScript;
            if (newScript == _typeEntry.TypeScript)
                return;

            Type newType = newScript != null ? newScript.GetClass() : null;
            if (newType == null || newType.IsAbstract || !typeof(MgDataBase).IsAssignableFrom(newType)) {
                EditorUtility.DisplayDialog(
                    "无法修改 MonoScript",
                    "请选择一个具体的 MgDataBase 子类脚本。",
                    "确定");
                RefreshUI();
                return;
            }

            MgDataKitAssetTypeEntry duplicate = _catalog.FindTypeEntry(newType);
            if (duplicate != null && duplicate != _typeEntry) {
                EditorUtility.DisplayDialog(
                    "无法修改 MonoScript",
                    $"{newType.Name} 已经有类型配置。",
                    "确定");
                RefreshUI();
                return;
            }

            if (_typeEntry.Assets.Count > 0) {
                EditorUtility.DisplayDialog(
                    "无法修改 MonoScript",
                    "已有 Asset 的类型项不能更换脚本。",
                    "确定");
                RefreshUI();
                return;
            }

            Undo.RecordObject(_catalog, "修改 MgData 类型脚本");
            _typeEntry.SetTypeScript(newScript);
            _selectedType = newType;
            SaveCatalogAndRefresh();
        }

        private void OnDataSourceChanged(ChangeEvent<string> evt) {
            if (_refreshing || _catalog == null || _typeEntry == null)
                return;

            int selectedIndex = _sourceNames.IndexOf(evt.newValue);
            if (selectedIndex < 0 || selectedIndex >= _sourceIds.Count) {
                RefreshUI();
                return;
            }

            string sourceId = _sourceIds[selectedIndex];
            if (string.Equals(sourceId, _typeEntry.SourceId, StringComparison.OrdinalIgnoreCase))
                return;

            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(sourceId);
            if (adapter == null) {
                EditorUtility.DisplayDialog("切换数据源", "当前数据源适配器不可用。", "确定");
                RefreshUI();
                return;
            }

            int affectedAssetCount = _typeEntry.Assets.Count(entry => entry?.Asset != null);
            if (!EditorUtility.DisplayDialog(
                    "切换数据源",
                    $"将 {_typeEntry.AssetType?.Name} 切换为 {adapter.DisplayName}，并清除 {affectedAssetCount} 个 Asset 的来源绑定。\n\n继续？",
                    "切换并清理",
                    "取消")) {
                RefreshUI();
                return;
            }

            Undo.RecordObject(_catalog, "切换 MgData 数据源");
            _typeEntry.SourceId = sourceId;
            for (var i = 0; i < _typeEntry.Assets.Count; i++) {
                MgDataKitAssetEntry assetEntry = _typeEntry.Assets[i];
                if (assetEntry?.Asset == null)
                    continue;

                assetEntry.SourceId = sourceId;
                assetEntry.SourceData = string.Empty;
                if (!adapter.TryInitializeBinding(assetEntry, out string bindingError))
                    Debug.LogWarning($"[MgDataKit] 来源绑定初始化失败：{bindingError}");
            }

            SaveCatalogAndRefresh();
        }

        private void RebuildSourceOptions() {
            _sourceIds.Clear();
            _sourceNames.Clear();
            IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
            for (var i = 0; i < adapters.Count; i++) {
                IMgDataSourceAdapter adapter = adapters[i];
                if (adapter == null || string.IsNullOrWhiteSpace(adapter.SourceId))
                    continue;

                string displayName = string.IsNullOrWhiteSpace(adapter.DisplayName)
                    ? adapter.SourceId
                    : adapter.DisplayName;
                if (_sourceNames.Contains(displayName))
                    displayName = $"{displayName} ({adapter.SourceId})";
                _sourceIds.Add(adapter.SourceId);
                _sourceNames.Add(displayName);
            }

            string currentSourceId = _typeEntry?.SourceId;
            if (!string.IsNullOrWhiteSpace(currentSourceId) &&
                !_sourceIds.Any(id => string.Equals(id, currentSourceId, StringComparison.OrdinalIgnoreCase))) {
                _sourceIds.Add(currentSourceId);
                _sourceNames.Add($"{currentSourceId}（适配器不可用）");
            }

            _dataSourceField.choices = new List<string>(_sourceNames);
        }

        private void ToggleTag(string tagId) {
            if (_catalog == null || _typeEntry == null)
                return;

            Undo.RecordObject(_catalog, "修改 MgData 表 Tags");
            if (!_typeEntry.Tags.Add(tagId))
                _typeEntry.Tags.Remove(tagId);
            SaveCatalogAndRefresh();
        }

        private void ClearTags() {
            if (_catalog == null || _typeEntry == null || _typeEntry.Tags.TagIds.Count == 0)
                return;

            Undo.RecordObject(_catalog, "清空 MgData 表 Tags");
            IReadOnlyList<string> tagIds = _typeEntry.Tags.TagIds;
            for (var i = tagIds.Count - 1; i >= 0; i--)
                _typeEntry.Tags.Remove(tagIds[i]);
            SaveCatalogAndRefresh();
        }

        private void RebuildTagMenu() {
            if (_tagMenu == null)
                return;

            _tagMenu.menu.MenuItems().Clear();
            int selectedCount = _typeEntry?.Tags.TagIds.Count ?? 0;
            _tagMenu.text = selectedCount == 0 ? "选择 Tags" : $"Tags ({selectedCount})";
            _tagMenu.SetEnabled(_catalog != null && _typeEntry != null);
            if (_catalog == null || _typeEntry == null)
                return;

            _tagMenu.menu.AppendAction(
                "清空 Tags",
                _ => ClearTags(),
                selectedCount == 0
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal);
            _tagMenu.menu.AppendSeparator();

            IReadOnlyList<DataTableTagDefinition> definitions = _catalog.TagSystem.Definitions;
            if (definitions.Count == 0) {
                _tagMenu.menu.AppendAction(
                    "暂无全局 Tag",
                    _ => DataTableTagManagementWindow.Open(),
                    DropdownMenuAction.Status.Disabled);
                return;
            }

            for (var i = 0; i < definitions.Count; i++) {
                DataTableTagDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                    continue;

                string tagId = definition.Id;
                _tagMenu.menu.AppendAction(
                    definition.Name,
                    _ => ToggleTag(tagId),
                    _typeEntry.Tags.Contains(tagId)
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal);
            }

            _tagMenu.menu.AppendSeparator();
            _tagMenu.menu.AppendAction(
                "管理全局 Tag",
                _ => DataTableTagManagementWindow.Open());
        }

        private void RefreshUI() {
            if (_typePopup == null)
                return;

            _refreshing = true;
            try {
                _typeNames.Clear();
                for (var i = 0; i < _types.Count; i++)
                    _typeNames.Add(GetTypeName(_types[i]));
                _typePopup.choices = new List<string>(_typeNames);

                int selectedIndex = _selectedType == null ? -1 : _types.IndexOf(_selectedType);
                if (selectedIndex >= 0)
                    _typePopup.SetValueWithoutNotify(_typeNames[selectedIndex]);
                _typePopup.SetEnabled(_types.Count > 0 && _catalog != null);

                _typeEntry = _catalog?.FindTypeEntry(_selectedType);
                bool hasEntry = _typeEntry != null;
                bool sourceAdapterMissing = hasEntry &&
                    MgDataSourceAdapterRegistry.Find(_typeEntry) == null;
                _helpBox.text = _catalog == null
                    ? (string.IsNullOrWhiteSpace(_loadError) ? "无法加载 MgData Catalog。" : _loadError)
                    : _types.Count == 0
                        ? "没有可配置的 MgDataBase 类型。"
                        : !hasEntry
                            ? "当前类型尚未注册到 MgData Catalog。"
                            : sourceAdapterMissing
                                ? $"当前数据源没有可用适配器：{_typeEntry.SourceId}。请加载对应的 Editor 模块。"
                                : string.Empty;
                _helpBox.style.display = string.IsNullOrWhiteSpace(_helpBox.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;

                string typeName = _selectedType != null ? _selectedType.Name : "MgData 表类型配置";
                _titleLabel.text = typeName;
                titleContent = new GUIContent($"MgData 类型配置 - {typeName}");
                _summaryLabel.text = hasEntry
                    ? $"{_selectedType.FullName} | {_typeEntry.Assets.Count} 个 Asset"
                    : string.Empty;

                _scriptField.SetValueWithoutNotify(_typeEntry?.TypeScript);
                _scriptField.SetEnabled(hasEntry);
                RebuildSourceOptions();
                int sourceIndex = hasEntry
                    ? _sourceIds.FindIndex(id => string.Equals(
                        id,
                        _typeEntry.SourceId,
                        StringComparison.OrdinalIgnoreCase))
                    : -1;
                _dataSourceField.SetValueWithoutNotify(
                    sourceIndex >= 0 ? _sourceNames[sourceIndex] : string.Empty);
                _dataSourceField.SetEnabled(hasEntry && _sourceNames.Count > 0);
                _tagSummaryLabel.text = BuildTagSummary();
                RebuildTagMenu();
            }
            finally {
                _refreshing = false;
            }
        }

        private string BuildTagSummary() {
            if (_catalog == null || _typeEntry == null)
                return "未选择表类型";

            var names = new List<string>();
            IReadOnlyList<string> tagIds = _typeEntry.Tags.TagIds;
            for (var i = 0; i < tagIds.Count; i++) {
                DataTableTagDefinition definition = _catalog.TagSystem.FindById(tagIds[i]);
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Name))
                    names.Add(definition.Name);
            }

            return names.Count == 0 ? "未选择 Tag" : string.Join(" / ", names);
        }

        private static string GetTypeName(Type type) {
            return type == null ? "(Missing Script)" : type.Name;
        }

        private void ReloadCatalog() {
            Type previousType = _selectedType;
            _catalog = null;
            _typeEntry = null;
            _types.Clear();
            _loadError = null;
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out _catalog,
                    out _loadError)) {
                RefreshUI();
                return;
            }

            for (var i = 0; i < _catalog.Entries.Count; i++) {
                Type assetType = _catalog.Entries[i]?.AssetType;
                if (assetType != null && !assetType.IsAbstract)
                    _types.Add(assetType);
            }

            _types.Sort((left, right) => string.Compare(
                left.Name,
                right.Name,
                StringComparison.Ordinal));
            _selectedType = previousType != null && _types.Contains(previousType)
                ? previousType
                : _types.FirstOrDefault();
            RefreshUI();
        }

        private void ScheduleCatalogReload() {
            EditorApplication.delayCall -= ReloadCatalog;
            EditorApplication.delayCall += ReloadCatalog;
        }

        private void HandleUndoRedo() {
            ReloadCatalog();
            MgDataKitEditor.RepaintOpenWindows();
        }

        private void SaveCatalogAndRefresh() {
            MgDataKitAssetCatalogProvider.Save(_catalog);
            MgDataKitEditor.RepaintOpenWindows();
            ReloadCatalog();
        }
    }
}
#endif
