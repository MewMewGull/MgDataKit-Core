using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    /// <summary>
    /// MgDataKit 编辑窗口：管理表类型、Asset 与可注册的数据源适配器。
    /// </summary>
    public class MgDataKitEditor : EditorWindow {
        private const string UssRelativePath = "Editor/Windows/MgDataKitEditor.uss";
        private const float DefaultLeftPanelWidth = 180f;
        private const float LeftPanelMinWidth = 140f;

        private List<Type> _allTypes;
        private readonly List<TypeListItem> _visibleTypeItems = new();
        private readonly HashSet<string> _selectedTagIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedSourceIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _tagFilterInitialized;
        private bool _sourceFilterInitialized;
        private string _typeLoadError;
        private string _typeSearchText = string.Empty;
        private Type _selectedType;
        private MgDataKitAssetCatalog _assetCatalog;
        private MgDataKitAssetTypeEntry _selectedTypeEntry;
        private readonly List<MgDataKitAssetEntry> _assetEntries = new();
        private readonly Dictionary<Type, MonoScript> _scriptsByType = new();
        private float _leftPanelWidth = DefaultLeftPanelWidth;

        private TwoPaneSplitView _mainSplit;
        private VisualElement _typePane;
        private ListView _typeListView;
        private ListView _assetListView;
        private ToolbarSearchField _typeSearchField;
        private ToolbarMenu _filterMenu;
        private Label _typeSummaryLabel;
        private HelpBox _typeLoadHelpBox;
        private VisualElement _assetPanel;
        private Label _assetTitleLabel;
        private ObjectField _scriptField;
        private Button _batchImportButton;
        private Button _createAssetButton;
        private Button _importCurrentTypeButton;
        private Button _typeConfigurationButton;
        private Label _assetSummaryLabel;
        private HelpBox _assetEmptyHelpBox;
        private VisualElement _leftExtensionActions;
        private VisualElement _assetExtensionActions;
        private VisualElement _assetEmptyExtensionContainer;
        private VisualElement _assetEmptyExtensionViews;
        private MgDataKitEditorExtensionRegistry _extensionRegistry;
        private MgDataKitEditorContext _editorContext;
        private EditorCommandService _commandService;
        private bool _extensionViewsBuilt;

        [MenuItem(MgDataKitEditorMenu.Data.MgDataTables, false, 0)]
        private static void Init() {
            MgDataKitEditor window = GetWindow<MgDataKitEditor>(false, "MgData 表编辑器");
            window.minSize = new Vector2(640f, 400f);
            window.Show();
        }

        private void OnEnable() {
            _leftPanelWidth = MgDataKitUserPreferencesStore.GetLeftPanelWidth(DefaultLeftPanelWidth);
            _extensionRegistry = MgDataKitEditorExtensionRegistry.Discover();
            _editorContext = new MgDataKitEditorContext {
                AssetEntries = _assetEntries,
                LastRefreshReason = EditorRefreshReason.InitialLayout
            };
            _commandService = new EditorCommandService(this);
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.projectChanged += ScheduleExternalCatalogChange;
            EnsureTypesLoaded();
            NotifyExtensionsCreated();
        }

        private void OnDisable() {
            NotifyExtensionsDestroyed();
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.projectChanged -= ScheduleExternalCatalogChange;
            EditorApplication.delayCall -= HandleProjectChanged;
        }

        private void CreateGUI() {
            rootVisualElement.Clear();
            _extensionViewsBuilt = false;
            rootVisualElement.style.flexGrow = 1f;
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                MgDataKitEditorAssetPaths.Resolve(UssRelativePath));
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            BuildMainLayout();
            EnsureTypesLoaded();
            RefreshTypeListView();
            RefreshAssetPanel();
            RefreshExtensionActions();
        }

        private void BuildMainLayout() {
            _mainSplit = new TwoPaneSplitView(0, _leftPanelWidth, TwoPaneSplitViewOrientation.Horizontal) {
                name = "mg-data-kit-main-split"
            };
            _mainSplit.style.flexGrow = 1f;

            _typePane = BuildTypePane();
            _typePane.RegisterCallback<GeometryChangedEvent>(OnTypePaneGeometryChanged);
            _assetPanel = BuildAssetPanel();
            _mainSplit.Add(_typePane);
            _mainSplit.Add(_assetPanel);
            rootVisualElement.Add(_mainSplit);
        }

        private VisualElement BuildTypePane() {
            VisualElement pane = new VisualElement { name = "mg-data-kit-type-pane" };
            pane.AddToClassList("mg-data-kit-pane");

            VisualElement heading = new VisualElement { name = "mg-data-kit-type-heading" };
            heading.AddToClassList("mg-data-kit-heading");
            Label title = new Label("MgData 类型");
            title.AddToClassList("mg-data-kit-heading-title");
            _typeSummaryLabel = new Label();
            _typeSummaryLabel.AddToClassList("mg-data-kit-heading-summary");
            heading.Add(title);
            heading.Add(_typeSummaryLabel);
            pane.Add(heading);

            VisualElement filters = new VisualElement { name = "mg-data-kit-type-filters" };
            filters.AddToClassList("mg-data-kit-filter-row");
            _typeSearchField = new ToolbarSearchField {
                name = "mg-data-kit-type-search",
                tooltip = "按类型名或 Tag 名搜索"
            };
            _typeSearchField.SetValueWithoutNotify(_typeSearchText);
            _typeSearchField.RegisterValueChangedCallback(evt => {
                _typeSearchText = evt.newValue ?? string.Empty;
                EnsureSelectedTypeVisible();
                RefreshTypeListView();
                RefreshAssetPanel();
            });
            filters.Add(_typeSearchField);

            _filterMenu = new ToolbarMenu {
                name = "mg-data-kit-filter-menu",
                text = GetFilterMenuLabel(),
                tooltip = "按 Tag 或数据源过滤"
            };
            _filterMenu.variant = ToolbarMenu.Variant.Popup;
            filters.Add(_filterMenu);
            pane.Add(filters);

            _typeLoadHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _typeLoadHelpBox.AddToClassList("mg-data-kit-help-box");
            _typeLoadHelpBox.style.display = DisplayStyle.None;
            pane.Add(_typeLoadHelpBox);

            _typeListView = new ListView {
                name = "mg-data-kit-type-list",
                itemsSource = _visibleTypeItems,
                selectionType = SelectionType.Single,
                fixedItemHeight = 48f,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = MakeTypeListItem,
                bindItem = BindTypeListItem
            };
            _typeListView.AddToClassList("mg-data-kit-list");
            _typeListView.selectionChanged += OnTypeSelectionChanged;
            _typeListView.itemsChosen += OnTypeItemsChosen;
            pane.Add(_typeListView);

            VisualElement actions = new VisualElement { name = "mg-data-kit-type-actions" };
            actions.AddToClassList("mg-data-kit-actions");
            Button refreshButton = new Button(RefreshTypes) { text = "刷新类型", tooltip = "重新读取 MgData 类型和 Tag" };
            Button importAllButton = new Button(ImportAllAssets) {
                text = "全量导入",
                tooltip = "按各类型来源适配器导入全部 Asset"
            };
            Button lintButton = new Button(RunLintValidation) { text = "手动执行 Lint", tooltip = "验证所有已导入表" };
            actions.Add(refreshButton);
            actions.Add(importAllButton);
            actions.Add(lintButton);
            // Optional integrations contribute to this slot through the extension registry.
            pane.Add(actions);
            _leftExtensionActions = BuildExtensionActionContainer(
                MgDataKitEditorActionSlot.LeftPaneActions,
                "mg-data-kit-left-extension-actions");
            pane.Add(_leftExtensionActions);
            return pane;
        }

        private VisualElement BuildAssetPanel() {
            VisualElement panel = new VisualElement { name = "mg-data-kit-asset-pane" };
            panel.AddToClassList("mg-data-kit-pane");

            VisualElement heading = new VisualElement { name = "mg-data-kit-asset-heading" };
            heading.AddToClassList("mg-data-kit-heading");
            _assetTitleLabel = new Label();
            _assetTitleLabel.AddToClassList("mg-data-kit-heading-title");
            _assetSummaryLabel = new Label();
            _assetSummaryLabel.AddToClassList("mg-data-kit-heading-summary");
            heading.Add(_assetTitleLabel);
            heading.Add(_assetSummaryLabel);
            panel.Add(heading);

            _scriptField = new ObjectField("脚本") {
                name = "mg-data-kit-script-field",
                objectType = typeof(MonoScript),
                allowSceneObjects = false
            };
            _scriptField.SetEnabled(false);
            panel.Add(_scriptField);

            VisualElement actions = new VisualElement { name = "mg-data-kit-asset-actions" };
            actions.AddToClassList("mg-data-kit-actions");
            _batchImportButton = new Button(OpenBatchImportWindow) {
                text = "批量导入",
                tooltip = "按当前类型批量创建或绑定 Asset"
            };
            _createAssetButton = new Button(CreateNewAsset) { text = "新建 Asset", tooltip = "创建一个新的 MgData Asset" };
            _importCurrentTypeButton = new Button(ImportCurrentTypeAssets) {
                text = "导入当前类型",
                tooltip = "导入当前类型的所有 Asset"
            };
            _typeConfigurationButton = new Button(OpenSelectedTypeConfiguration) {
                text = "打开类型配置",
                tooltip = "查看当前表类型详细信息并配置 Tags"
            };
            actions.Add(_batchImportButton);
            actions.Add(_createAssetButton);
            actions.Add(_importCurrentTypeButton);
            actions.Add(_typeConfigurationButton);
            panel.Add(actions);
            _assetExtensionActions = BuildExtensionActionContainer(
                MgDataKitEditorActionSlot.AssetPaneActions,
                "mg-data-kit-asset-extension-actions");
            panel.Add(_assetExtensionActions);

            _assetEmptyHelpBox = new HelpBox("左侧选择 MgDataBase 子类类型", HelpBoxMessageType.Info);
            _assetEmptyHelpBox.AddToClassList("mg-data-kit-help-box");
            panel.Add(_assetEmptyHelpBox);
            _assetEmptyExtensionContainer = new VisualElement {
                name = "mg-data-kit-asset-empty-extensions"
            };
            _assetEmptyExtensionContainer.AddToClassList("mg-data-kit-actions");
            panel.Add(_assetEmptyExtensionContainer);
            _assetEmptyExtensionViews = new VisualElement {
                name = "mg-data-kit-asset-empty-extension-views"
            };
            panel.Add(_assetEmptyExtensionViews);

            _assetListView = new ListView {
                name = "mg-data-kit-asset-list",
                itemsSource = _assetEntries,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeAssetListItem,
                bindItem = BindAssetListItem,
                reorderable = false
            };
            _assetListView.AddToClassList("mg-data-kit-list");
            _assetListView.itemIndexChanged += OnAssetListItemIndexChanged;
            _assetListView.selectionChanged += OnAssetSelectionChanged;
            panel.Add(_assetListView);
            return panel;
        }

        private VisualElement BuildExtensionActionContainer(
            MgDataKitEditorActionSlot slot,
            string name) {
            var container = new VisualElement { name = name };
            container.AddToClassList("mg-data-kit-actions");
            return container;
        }

        private void RefreshExtensionActions() {
            BuildExtensionActions(
                _leftExtensionActions,
                MgDataKitEditorActionSlot.LeftPaneActions);
            BuildExtensionActions(
                _assetExtensionActions,
                MgDataKitEditorActionSlot.AssetPaneActions);
            BuildExtensionActions(
                _assetEmptyExtensionContainer,
                MgDataKitEditorActionSlot.AssetEmptyState);
            if (!_extensionViewsBuilt) {
                BuildExtensionViews(
                    _assetEmptyExtensionViews,
                    MgDataKitEditorActionSlot.AssetEmptyState);
                _extensionViewsBuilt = true;
            }
            RefreshExtensionViews();
        }

        private void BuildExtensionActions(
            VisualElement container,
            MgDataKitEditorActionSlot slot) {
            if (container == null || _extensionRegistry == null)
                return;

            container.Clear();
            IReadOnlyList<MgDataKitEditorActionDefinition> definitions = _extensionRegistry.GetActions(slot);
            for (var i = 0; i < definitions.Count; i++) {
                MgDataKitEditorActionDefinition definition = definitions[i];
                if (definition.IsVisible != null && !definition.IsVisible(_editorContext))
                    continue;

                var button = new Button(() => definition.Execute?.Invoke(_editorContext, _commandService)) {
                    name = $"mg-data-kit-extension-action-{definition.Id}",
                    text = definition.Text,
                    tooltip = definition.Tooltip
                };
                button.SetEnabled(definition.IsEnabled == null || definition.IsEnabled(_editorContext));
                container.Add(button);
            }
        }

        private void BuildExtensionViews(
            VisualElement container,
            MgDataKitEditorActionSlot slot) {
            if (container == null || _extensionRegistry == null)
                return;

            container.Clear();
            IReadOnlyList<IMgDataKitEditorViewExtension> extensions =
                _extensionRegistry.GetViews(slot);
            for (var i = 0; i < extensions.Count; i++) {
                IMgDataKitEditorViewExtension extension = extensions[i];
                var viewContainer = new VisualElement {
                    name = $"mg-data-kit-extension-view-{extension.Id}"
                };
                container.Add(viewContainer);
                try {
                    extension.Build(_editorContext, viewContainer);
                    viewContainer.style.display = extension.IsVisible(_editorContext)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 视图扩展构建失败：{extension.Id}\n{ex}");
                }
            }
        }

        private void RefreshExtensionViews() {
            if (_extensionRegistry == null || _assetEmptyExtensionViews == null)
                return;

            IReadOnlyList<IMgDataKitEditorViewExtension> extensions =
                _extensionRegistry.GetViews(MgDataKitEditorActionSlot.AssetEmptyState);
            for (var i = 0; i < extensions.Count; i++) {
                VisualElement viewContainer = _assetEmptyExtensionViews.Q<VisualElement>(
                    $"mg-data-kit-extension-view-{extensions[i].Id}");
                if (viewContainer == null)
                    continue;

                try {
                    extensions[i].Refresh(_editorContext, viewContainer);
                    viewContainer.style.display = extensions[i].IsVisible(_editorContext)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 视图扩展刷新失败：{extensions[i].Id}\n{ex}");
                }
            }
        }

        private void NotifyExtensionsCreated() {
            if (_extensionRegistry == null)
                return;

            IReadOnlyList<IMgDataKitEditorLifecycleExtension> extensions =
                _extensionRegistry.LifecycleExtensions;
            for (var i = 0; i < extensions.Count; i++) {
                try {
                    extensions[i].OnWindowCreated(_editorContext);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 生命周期扩展初始化失败：{extensions[i].Id}\n{ex}");
                }
            }
        }

        private void NotifyExtensionsRefreshed(EditorRefreshReason reason) {
            if (_extensionRegistry == null)
                return;

            _editorContext.LastRefreshReason = reason;
            IReadOnlyList<IMgDataKitEditorLifecycleExtension> extensions =
                _extensionRegistry.LifecycleExtensions;
            for (var i = 0; i < extensions.Count; i++) {
                try {
                    extensions[i].OnRefresh(_editorContext, reason);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 生命周期扩展刷新失败：{extensions[i].Id}\n{ex}");
                }
            }
        }

        private void NotifyExtensionsDestroyed() {
            if (_extensionRegistry == null || _editorContext == null)
                return;

            IReadOnlyList<IMgDataKitEditorLifecycleExtension> extensions =
                _extensionRegistry.LifecycleExtensions;
            for (var i = 0; i < extensions.Count; i++) {
                try {
                    extensions[i].OnWindowDestroyed(_editorContext);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 生命周期扩展销毁失败：{extensions[i].Id}\n{ex}");
                }
            }
        }

        private void OnTypePaneGeometryChanged(GeometryChangedEvent evt) {
            float width = evt.newRect.width;
            if (width < LeftPanelMinWidth || Mathf.Approximately(width, _leftPanelWidth))
                return;

            _leftPanelWidth = width;
            MgDataKitUserPreferencesStore.SetLeftPanelWidth(_leftPanelWidth);
        }

        private void EnsureTypesLoaded() {
            if (_allTypes == null)
                RefreshTypes();
        }

        private string GetFilterMenuLabel() {
            bool allTags = _selectedTagIds.Count == 0 || AreAllTagsSelected();
            bool allSources = _selectedSourceIds.Count == 0 || AreAllSourcesSelected();
            return allTags && allSources ? "全部" : "筛选";
        }

        private static string GetSourceFilterDisplayName(IMgDataSourceAdapter adapter) {
            if (adapter == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(adapter.DisplayName)
                ? adapter.SourceId
                : adapter.DisplayName;
        }

        private VisualElement MakeTypeListItem() {
            VisualElement row = new VisualElement { name = "mg-data-kit-type-row" };
            row.AddToClassList("mg-data-kit-type-row");
            Label typeLabel = new Label { name = "mg-data-kit-type-name" };
            typeLabel.AddToClassList("mg-data-kit-type-name");
            Label tagLabel = new Label { name = "mg-data-kit-type-tags" };
            tagLabel.AddToClassList("mg-data-kit-type-tags");
            row.Add(typeLabel);
            row.Add(tagLabel);
            return row;
        }

        private void BindTypeListItem(VisualElement element, int index) {
            if (index < 0 || index >= _visibleTypeItems.Count)
                return;

            TypeListItem item = _visibleTypeItems[index];
            Label typeLabel = element.Q<Label>("mg-data-kit-type-name");
            Label tagLabel = element.Q<Label>("mg-data-kit-type-tags");
            typeLabel.text = item.TypeName;
            tagLabel.text = item.TagText;
            element.EnableInClassList("mg-data-kit-type-row-selected", _selectedType == item.Type);
        }

        private void OnTypeSelectionChanged(IEnumerable<object> selectedItems) {
            TypeListItem item = selectedItems?.OfType<TypeListItem>().FirstOrDefault();
            Type selectedType = item?.Type;
            if (selectedType == null || selectedType == _selectedType)
                return;

            _selectedType = selectedType;
            RefreshAssets();
            RefreshAssetPanel();
            _typeListView?.RefreshItems();
            NotifyExtensionsRefreshed(EditorRefreshReason.TypeSelectionChanged);
        }

        private void OnTypeItemsChosen(IEnumerable<object> chosenItems) {
            TypeListItem item = chosenItems?.OfType<TypeListItem>().FirstOrDefault();
            if (item?.Type != null)
                DataTableTypeConfigurationWindow.Open(item.Type);
        }

        private void RebuildFilterMenu() {
            if (_filterMenu == null)
                return;

            _filterMenu.text = GetFilterMenuLabel();
            _filterMenu.menu.MenuItems().Clear();
            bool canFilter = _assetCatalog != null;
            _filterMenu.SetEnabled(canFilter);
            if (!canFilter)
                return;

            IReadOnlyList<DataTableTagDefinition> definitions = _assetCatalog.TagSystem.Definitions;
            string[] tagIds = definitions
                .Where(definition => definition != null)
                .Select(definition => definition.Id)
                .ToArray();
            bool allTagsSelected = tagIds.Length > 0 && AreAllTagsSelected();
            DropdownMenuAction.Status allTagsStatus = tagIds.Length == 0
                ? DropdownMenuAction.Status.Disabled
                : allTagsSelected
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal;
            _filterMenu.menu.AppendAction(
                "全部Tag",
                _ => SelectAllTagFilters(tagIds),
                allTagsStatus);
            _filterMenu.menu.AppendSeparator();

            if (tagIds.Length == 0) {
                _filterMenu.menu.AppendAction(
                    "暂无 Tag",
                    _ => { },
                    DropdownMenuAction.Status.Disabled);
            }
            else {
                for (var i = 0; i < definitions.Count; i++) {
                    DataTableTagDefinition definition = definitions[i];
                    if (definition == null)
                        continue;

                    string tagId = definition.Id;
                    _filterMenu.menu.AppendAction(
                        definition.Name,
                        _ => ToggleTagFilter(tagId),
                        _selectedTagIds.Contains(tagId)
                            ? DropdownMenuAction.Status.Checked
                            : DropdownMenuAction.Status.Normal);
                }
            }

            _filterMenu.menu.AppendSeparator();

            IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
            var sourceIds = new List<string>();
            for (var i = 0; i < adapters.Count; i++) {
                IMgDataSourceAdapter adapter = adapters[i];
                if (adapter == null || string.IsNullOrWhiteSpace(adapter.SourceId) ||
                    sourceIds.Contains(adapter.SourceId, StringComparer.OrdinalIgnoreCase))
                    continue;

                sourceIds.Add(adapter.SourceId);
            }

            bool allSourcesSelected = sourceIds.Count > 0 && AreAllSourcesSelected();
            DropdownMenuAction.Status allSourcesStatus = sourceIds.Count == 0
                ? DropdownMenuAction.Status.Disabled
                : allSourcesSelected
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal;
            _filterMenu.menu.AppendAction(
                "全部源",
                _ => SelectAllSourceFilters(sourceIds),
                allSourcesStatus);
            _filterMenu.menu.AppendSeparator();

            if (sourceIds.Count == 0) {
                _filterMenu.menu.AppendAction(
                    "暂无来源适配器",
                    _ => { },
                    DropdownMenuAction.Status.Disabled);
            }
            else {
                for (var i = 0; i < adapters.Count; i++) {
                    IMgDataSourceAdapter adapter = adapters[i];
                    if (adapter == null || string.IsNullOrWhiteSpace(adapter.SourceId) ||
                        !sourceIds.Contains(adapter.SourceId, StringComparer.OrdinalIgnoreCase))
                        continue;

                    string sourceId = adapter.SourceId;
                    _filterMenu.menu.AppendAction(
                        GetSourceFilterDisplayName(adapter),
                        _ => ToggleSourceFilter(sourceId),
                        _selectedSourceIds.Contains(sourceId)
                            ? DropdownMenuAction.Status.Checked
                            : DropdownMenuAction.Status.Normal);
                }
            }

            _filterMenu.menu.AppendSeparator();
            _filterMenu.menu.AppendAction(
                "全局Tag配置",
                _ => DataTableTagManagementWindow.Open());
        }

        private void SelectAllTagFilters(IReadOnlyList<string> tagIds) {
            _selectedTagIds.Clear();
            for (var i = 0; i < tagIds.Count; i++)
                _selectedTagIds.Add(tagIds[i]);
            ApplyTagFilterChange();
        }

        private void ToggleTagFilter(string tagId) {
            if (!_selectedTagIds.Add(tagId))
                _selectedTagIds.Remove(tagId);
            ApplyTagFilterChange();
        }

        private void SelectAllSourceFilters(IReadOnlyList<string> sourceIds) {
            _selectedSourceIds.Clear();
            for (var i = 0; i < sourceIds.Count; i++)
                _selectedSourceIds.Add(sourceIds[i]);
            ApplySourceFilterChange();
        }

        private void ToggleSourceFilter(string sourceId) {
            if (string.IsNullOrWhiteSpace(sourceId))
                return;

            if (!_selectedSourceIds.Add(sourceId))
                _selectedSourceIds.Remove(sourceId);
            ApplySourceFilterChange();
        }

        private void ApplyTagFilterChange() {
            EnsureSelectedTypeVisible();
            RefreshTypeListView();
            RefreshAssetPanel();
        }

        private void ApplySourceFilterChange() {
            EnsureSelectedTypeVisible();
            RefreshTypeListView();
            RefreshAssetPanel();
        }

        private static bool ShouldDisplayAsset(MgDataKitAssetEntry entry) {
            return entry?.Asset != null;
        }

        private void RefreshAssetPanel() {
            if (_assetTitleLabel == null)
                return;

            UpdateEditorContext();

            bool hasType = _selectedType != null;
            _assetTitleLabel.text = hasType ? $"{_selectedType.Name} Assets" : "MgData Asset";
            _assetSummaryLabel.text = hasType ? $"{_assetEntries.Count} 个 Asset" : string.Empty;
            _scriptField.value = hasType ? GetMonoScriptForType(_selectedType) : null;
            _batchImportButton.SetEnabled(hasType && CanOpenBatchImport());
            _createAssetButton.SetEnabled(hasType);
            _importCurrentTypeButton.SetEnabled(hasType && _assetEntries.Count > 0);
            _typeConfigurationButton.SetEnabled(hasType);

            bool showEmptyHelp = !hasType || _assetEntries.Count == 0;
            _assetEmptyHelpBox.text = !hasType
                ? "左侧选择 MgDataBase 子类类型"
                : "当前类型没有已绑定的 Asset。可以新建 Asset 或批量导入。";
            _assetEmptyHelpBox.style.display = showEmptyHelp ? DisplayStyle.Flex : DisplayStyle.None;
            _assetListView.style.display = hasType && _assetEntries.Count > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _assetListView.reorderable = hasType && _assetEntries.Count > 1;
            _assetListView?.RefreshItems();
            RefreshExtensionActions();
        }

        private VisualElement MakeAssetListItem() {
            VisualElement row = new VisualElement { name = "mg-data-kit-asset-row" };
            row.AddToClassList("mg-data-kit-asset-row");

            ObjectField assetField = new ObjectField("Asset") {
                name = "mg-data-kit-asset-field",
                objectType = typeof(MgDataBase),
                allowSceneObjects = false
            };
            assetField.SetEnabled(false);
            row.Add(assetField);

            VisualElement extensionSourceContainer = new VisualElement {
                name = "mg-data-kit-asset-extension-source"
            };
            extensionSourceContainer.AddToClassList("mg-data-kit-asset-extension-row");
            row.Add(extensionSourceContainer);

            VisualElement sourceAdapterContainer = new VisualElement {
                name = "mg-data-kit-source-adapter-binding"
            };
            sourceAdapterContainer.AddToClassList("mg-data-kit-asset-extension-row");
            row.Add(sourceAdapterContainer);

            VisualElement actionRow = new VisualElement { name = "mg-data-kit-asset-actions-row" };
            actionRow.AddToClassList("mg-data-kit-asset-actions-row");
            Button importButton = new Button { name = "mg-data-kit-asset-import" };
            Button openSourceButton = new Button {
                name = "mg-data-kit-asset-open-source",
                text = "打开源",
                tooltip = "打开当前绑定的数据源"
            };
            openSourceButton.AddToClassList("mg-data-kit-fixed-action-button");
            Button removeButton = new Button {
                name = "mg-data-kit-asset-remove",
                text = "移除引用",
                tooltip = "仅从 MgData Catalog 移除引用，不删除 Asset 或源文件"
            };
            removeButton.AddToClassList("mg-data-kit-danger-button");
            actionRow.Add(importButton);
            actionRow.Add(openSourceButton);
            actionRow.Add(removeButton);
            row.Add(actionRow);

            VisualElement extensionActionsContainer = new VisualElement {
                name = "mg-data-kit-asset-extension-actions-row"
            };
            extensionActionsContainer.AddToClassList("mg-data-kit-asset-actions-row");
            row.Add(extensionActionsContainer);

            AssetRowView rowView = new AssetRowView(
                assetField,
                importButton,
                openSourceButton,
                removeButton,
                extensionSourceContainer,
                extensionActionsContainer,
                sourceAdapterContainer);
            row.userData = rowView;
            BuildAssetRowExtensions(row, rowView);
            BuildRegisteredRowActions(rowView);
            UpdateAssetRowExtensionContainerVisibility(rowView);
            importButton.clicked += () => {
                if (row.userData is AssetRowView view)
                    ImportAsset(view.Entry);
            };
            openSourceButton.clicked += () => {
                if (row.userData is AssetRowView view)
                    OpenAssetSource(view.Entry);
            };
            removeButton.clicked += () => {
                if (row.userData is AssetRowView view)
                    RemoveAssetReference(view.Entry?.Asset);
            };
            return row;
        }

        private void BindAssetListItem(VisualElement element, int index) {
            if (index < 0 || index >= _assetEntries.Count)
                return;

            if (!(element.userData is AssetRowView view))
                return;

            MgDataKitAssetEntry entry = _assetEntries[index];
            MgDataBase asset = entry?.Asset;
            view.Entry = entry;
            view.AssetField.SetValueWithoutNotify(asset);

            // Source-specific validation and binding state belong to the selected adapter.
            bool canImport = asset != null;
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            view.ImportButton.text = "导入";
            view.ImportButton.tooltip = "根据当前类型的数据源导入 Asset";
            view.ImportButton.SetEnabled(canImport);
            bool hasSource = adapter != null && adapter.TryValidate(entry, out _);
            view.OpenSourceButton.tooltip = adapter == null
                ? "当前数据源适配器不可用"
                : $"打开{adapter.DisplayName}来源";
            view.OpenSourceButton.SetEnabled(asset != null && hasSource);
            bool canRemove = asset != null;
            view.RemoveButton.style.display = canRemove ? DisplayStyle.Flex : DisplayStyle.None;
            view.RemoveButton.SetEnabled(canRemove);
            BindSourceAdapter(view, entry, element);
            BindAssetRowExtensions(element, view, entry);
        }

        private void BindSourceAdapter(
            AssetRowView view,
            MgDataKitAssetEntry entry,
            VisualElement row) {
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            if (adapter == null) {
                view.SourceAdapterContainer.Clear();
                view.SourceAdapterBuilt = false;
                view.SourceAdapterId = null;
                view.SourceAdapterContainer.style.display = DisplayStyle.None;
                return;
            }

            view.SourceAdapterContainer.style.display = DisplayStyle.Flex;
            if (view.SourceAdapterContext == null) {
                view.SourceAdapterContext = new MgDataSourceAdapterContext {
                    Editor = _editorContext,
                    Commands = _commandService,
                    Row = new MgDataKitAssetRowContext {
                        Editor = _editorContext,
                        Row = row,
                        SourceContainer = view.SourceAdapterContainer,
                        ActionsContainer = view.ExtensionActionsContainer
                    }
                };
            }

            view.SourceAdapterContext.Editor = _editorContext;
            view.SourceAdapterContext.Commands = _commandService;
            view.SourceAdapterContext.Row.Editor = _editorContext;
            view.SourceAdapterContext.Row.Entry = entry;
            view.SourceAdapterContext.Row.Row = row;
            view.SourceAdapterContext.Row.SourceContainer = view.SourceAdapterContainer;
            view.SourceAdapterContext.Row.ActionsContainer = view.ExtensionActionsContainer;
            try {
                if (!view.SourceAdapterBuilt || !string.Equals(view.SourceAdapterId, adapter.SourceId, StringComparison.OrdinalIgnoreCase)) {
                    view.SourceAdapterContainer.Clear();
                    adapter.BuildBindingUI(view.SourceAdapterContext, view.SourceAdapterContainer);
                    view.SourceAdapterBuilt = true;
                    view.SourceAdapterId = adapter.SourceId;
                }
                adapter.BindBindingUI(view.SourceAdapterContext, view.SourceAdapterContainer);
            }
            catch (Exception ex) {
                Debug.LogError($"[MgDataKit] 来源适配器 UI 绑定失败：{adapter.SourceId}\n{ex}");
            }
        }

        private void BuildAssetRowExtensions(VisualElement row, AssetRowView view) {
            if (_extensionRegistry == null)
                return;

            IReadOnlyList<IMgDataKitAssetRowExtension> extensions =
                _extensionRegistry.AssetRowExtensions;
            for (var i = 0; i < extensions.Count; i++) {
                IMgDataKitAssetRowExtension extension = extensions[i];
                var sourceContainer = new VisualElement {
                    name = $"mg-data-kit-asset-row-extension-source-{extension.Id}"
                };
                sourceContainer.AddToClassList("mg-data-kit-asset-extension");
                sourceContainer.style.display = DisplayStyle.None;
                var actionsContainer = new VisualElement {
                    name = $"mg-data-kit-asset-row-extension-actions-{extension.Id}"
                };
                actionsContainer.AddToClassList("mg-data-kit-asset-extension");
                actionsContainer.style.display = DisplayStyle.None;
                view.ExtensionHosts.Add(new AssetRowExtensionHost(
                    extension,
                    sourceContainer,
                    actionsContainer));
                view.ExtensionSourceContainer.Add(sourceContainer);
                view.ExtensionActionsContainer.Add(actionsContainer);
                var context = new MgDataKitAssetRowContext {
                    Editor = _editorContext,
                    Row = row,
                    SourceContainer = sourceContainer,
                    ActionsContainer = actionsContainer
                };
                AssetRowExtensionHost host = view.ExtensionHosts[view.ExtensionHosts.Count - 1];
                host.Context = context;
                try {
                    extension.Build(context);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Asset 行扩展构建失败：{extension.Id}\n{ex}");
                }
            }
        }

        private void BuildRegisteredRowActions(AssetRowView view) {
            if (_extensionRegistry == null)
                return;

            BuildRegisteredRowActions(
                view.ExtensionSourceContainer,
                view,
                MgDataKitEditorActionSlot.AssetRowSource);
            BuildRegisteredRowActions(
                view.ExtensionActionsContainer,
                view,
                MgDataKitEditorActionSlot.AssetRowActions);
        }

        private void BuildRegisteredRowActions(
            VisualElement container,
            AssetRowView view,
            MgDataKitEditorActionSlot slot) {
            IReadOnlyList<MgDataKitEditorActionDefinition> definitions =
                _extensionRegistry.GetActions(slot);
            for (var i = 0; i < definitions.Count; i++) {
                MgDataKitEditorActionDefinition definition = definitions[i];
                if (definition.IsVisible != null && !definition.IsVisible(_editorContext))
                    continue;

                var button = new Button(() => ExecuteRowAction(definition, view.Entry)) {
                    name = $"mg-data-kit-row-extension-action-{definition.Id}",
                    text = definition.Text,
                    tooltip = definition.Tooltip
                };
                button.SetEnabled(definition.IsEnabled == null || definition.IsEnabled(_editorContext));
                container.Add(button);
            }
        }

        private void ExecuteRowAction(
            MgDataKitEditorActionDefinition definition,
            MgDataKitAssetEntry entry) {
            if (definition?.Execute == null || entry == null)
                return;

            var context = new MgDataKitEditorContext {
                Catalog = _assetCatalog,
                SelectedType = _selectedType,
                SelectedTypeEntry = _selectedTypeEntry,
                AssetEntries = _assetEntries,
                SelectedAsset = entry.Asset,
                LastRefreshReason = EditorRefreshReason.ExternalRequest
            };
            if (definition.IsEnabled != null && !definition.IsEnabled(context))
                return;

            definition.Execute(context, _commandService);
        }

        private void BindAssetRowExtensions(
            VisualElement row,
            AssetRowView view,
            MgDataKitAssetEntry entry) {
            if (view.ExtensionHosts.Count == 0)
                return;

            for (var i = 0; i < view.ExtensionHosts.Count; i++) {
                AssetRowExtensionHost host = view.ExtensionHosts[i];
                host.Context.Entry = entry;
                host.Context.Editor = _editorContext;
                host.Context.SourceContainer = host.SourceContainer;
                host.Context.ActionsContainer = host.ActionsContainer;
                bool visible = false;
                try {
                    visible = host.Extension.IsVisible(host.Context);
                    host.Extension.Bind(host.Context);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Asset 行扩展绑定失败：{host.Extension.Id}\n{ex}");
                }

                host.SourceContainer.style.display = visible && host.SourceContainer.childCount > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                host.ActionsContainer.style.display = visible && host.ActionsContainer.childCount > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            UpdateAssetRowExtensionContainerVisibility(view);
        }

        private static void UpdateAssetRowExtensionContainerVisibility(AssetRowView view) {
            if (view == null)
                return;

            SetContainerVisibleWhenPopulated(view.ExtensionSourceContainer);
            SetContainerVisibleWhenPopulated(view.ExtensionActionsContainer);
        }

        private static void SetContainerVisibleWhenPopulated(VisualElement container) {
            if (container == null)
                return;

            bool hasVisibleChild = false;
            for (var i = 0; i < container.childCount; i++) {
                if (container[i].style.display.value == DisplayStyle.None)
                    continue;

                hasVisibleChild = true;
                break;
            }

            container.style.display = hasVisibleChild ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OpenAssetSource(MgDataKitAssetEntry entry) {
            MgDataBase asset = entry?.Asset;
            if (asset == null)
                return;

            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            if (adapter != null) {
                if (!adapter.TryOpenSource(entry, out string adapterError))
                    EditorUtility.DisplayDialog("打开源失败", adapterError, "确定");
                return;
            }
            EditorUtility.DisplayDialog(
                "打开源失败",
                "当前数据源没有可用的来源适配器。",
                "确定");
        }

        private void ImportAsset(MgDataKitAssetEntry entry) {
            MgDataBase asset = entry?.Asset;
            if (asset == null)
                return;

            if (MgDataImportService.Import(asset)) {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            else {
                Debug.LogError($"[MgDataKit] Asset 导入失败: {asset.name}");
            }

            RefreshAssets();
            RefreshAssetPanel();
        }

        private void RemoveAssetReference(MgDataBase asset) {
            if (asset == null || _assetCatalog == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "移除 MgData Asset 引用",
                    $"确定从 MgData Catalog 移除“{asset.name}”的引用吗？\n\n" +
                    "Unity Asset 和绑定的数据源文件都不会被删除。",
                    "移除引用",
                    "取消"))
                return;

            Undo.RecordObject(_assetCatalog, "移除 MgData Asset 引用");
            if (!MgDataKitAssetCatalogProvider.RemoveAssetReference(asset))
                return;

            RefreshAssets();
        }

        private MonoScript GetMonoScriptForType(Type type) {
            if (type == null)
                return null;

            if (_scriptsByType.TryGetValue(type, out var cachedScript) && cachedScript != null)
                return cachedScript;

            ScriptableObject temp = CreateInstance(type);
            try {
                var script = MonoScript.FromScriptableObject(temp);
                _scriptsByType[type] = script;
                return script;
            }
            finally {
                DestroyImmediate(temp);
            }
        }

        private void RefreshTypes() {
            _allTypes = new List<Type>();
            _typeLoadError = null;
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out _assetCatalog,
                    out _typeLoadError)) {
                EnsureSelectedTypeVisible();
                RefreshTypeListView();
                RebuildFilterMenu();
                RefreshAssetPanel();
                return;
            }

            for (var i = 0; i < _assetCatalog.Entries.Count; i++) {
                Type assetType = _assetCatalog.Entries[i]?.AssetType;
                if (assetType != null && !assetType.IsAbstract)
                    _allTypes.Add(assetType);
            }

            _allTypes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            PruneSelectedTagIds();
            InitializeTagFilters();
            PruneSelectedSourceIds();
            InitializeSourceFilters();
            EnsureSelectedTypeVisible();
            if (_selectedType != null)
                RefreshAssets();
            RefreshTypeListView();
            RebuildFilterMenu();
            RefreshAssetPanel();
        }

        private bool IsTypeVisible(Type type) {
            if (type == null)
                return false;

            MgDataKitAssetTypeEntry typeEntry = _assetCatalog?.FindTypeEntry(type);
            if (typeEntry == null)
                return false;

            if (_selectedTagIds.Count > 0 && !AreAllTagsSelected()) {
                foreach (string tagId in _selectedTagIds) {
                    if (!typeEntry.Tags.Contains(tagId))
                        return false;
                }
            }

            if (_selectedSourceIds.Count > 0 && !AreAllSourcesSelected()) {
                if (!_selectedSourceIds.Contains(typeEntry.SourceId))
                    return false;
            }

            return true;
        }

        private void PruneSelectedTagIds() {
            if (_assetCatalog == null) {
                _selectedTagIds.Clear();
                return;
            }

            _selectedTagIds.RemoveWhere(tagId => _assetCatalog.TagSystem.FindById(tagId) == null);
        }

        private void PruneSelectedSourceIds() {
            IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
            if (adapters.Count == 0) {
                _selectedSourceIds.Clear();
                return;
            }

            var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < adapters.Count; i++) {
                IMgDataSourceAdapter adapter = adapters[i];
                if (adapter != null && !string.IsNullOrWhiteSpace(adapter.SourceId))
                    sourceIds.Add(adapter.SourceId);
            }

            _selectedSourceIds.RemoveWhere(sourceId => !sourceIds.Contains(sourceId));
        }

        private void InitializeTagFilters() {
            if (_tagFilterInitialized || _assetCatalog == null)
                return;

            _selectedTagIds.Clear();
            IReadOnlyList<DataTableTagDefinition> definitions = _assetCatalog.TagSystem.Definitions;
            for (var i = 0; i < definitions.Count; i++) {
                DataTableTagDefinition definition = definitions[i];
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Id))
                    _selectedTagIds.Add(definition.Id);
            }

            _tagFilterInitialized = true;
        }

        private void InitializeSourceFilters() {
            if (_sourceFilterInitialized)
                return;

            _selectedSourceIds.Clear();
            IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
            for (var i = 0; i < adapters.Count; i++) {
                IMgDataSourceAdapter adapter = adapters[i];
                if (adapter != null && !string.IsNullOrWhiteSpace(adapter.SourceId))
                    _selectedSourceIds.Add(adapter.SourceId);
            }

            _sourceFilterInitialized = true;
        }

        private bool AreAllTagsSelected() {
            if (_assetCatalog == null)
                return false;

            var tagCount = 0;
            IReadOnlyList<DataTableTagDefinition> definitions = _assetCatalog.TagSystem.Definitions;
            for (var i = 0; i < definitions.Count; i++) {
                DataTableTagDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    continue;

                tagCount++;
                if (!_selectedTagIds.Contains(definition.Id))
                    return false;
            }

            return tagCount > 0 && _selectedTagIds.Count == tagCount;
        }

        private bool AreAllSourcesSelected() {
            IReadOnlyList<IMgDataSourceAdapter> adapters = MgDataSourceAdapterRegistry.GetAll();
            var sourceCount = 0;
            for (var i = 0; i < adapters.Count; i++) {
                IMgDataSourceAdapter adapter = adapters[i];
                if (adapter == null || string.IsNullOrWhiteSpace(adapter.SourceId))
                    continue;

                sourceCount++;
                if (!_selectedSourceIds.Contains(adapter.SourceId))
                    return false;
            }

            return sourceCount > 0 && _selectedSourceIds.Count == sourceCount;
        }

        private void EnsureSelectedTypeVisible() {
            if (_selectedType == null)
                return;
            if (_allTypes != null &&
                _allTypes.Contains(_selectedType) &&
                IsTypeVisible(_selectedType) &&
                MatchesTypeSearch(_selectedType))
                return;

            _selectedType = null;
            RefreshAssets();
        }

        private void RefreshTypeListView() {
            if (_typeListView == null)
                return;

            Type previousSelection = _selectedType;
            _visibleTypeItems.Clear();
            if (_allTypes != null) {
                for (var i = 0; i < _allTypes.Count; i++) {
                    Type type = _allTypes[i];
                    if (!IsTypeVisible(type) || !MatchesTypeSearch(type))
                        continue;

                    MgDataKitAssetTypeEntry entry = _assetCatalog?.FindTypeEntry(type);
                    _visibleTypeItems.Add(new TypeListItem(type, BuildTagText(entry)));
                }
            }

            _typeListView.Rebuild();
            _typeLoadHelpBox.text = _typeLoadError ?? string.Empty;
            _typeLoadHelpBox.style.display = string.IsNullOrWhiteSpace(_typeLoadError)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            _typeSummaryLabel.text = _allTypes == null
                ? string.Empty
                : $"{_visibleTypeItems.Count}/{_allTypes.Count}";
            RebuildFilterMenu();

            int selectedIndex = previousSelection == null
                ? -1
                : _visibleTypeItems.FindIndex(item => item.Type == previousSelection);
            if (selectedIndex >= 0) {
                _typeListView.SetSelectionWithoutNotify(new[] { selectedIndex });
                _typeListView.schedule.Execute(() => {
                    if (_typeListView == null || selectedIndex >= _visibleTypeItems.Count)
                        return;
                    _typeListView.ScrollToItem(selectedIndex);
                });
            }
            else {
                _typeListView.ClearSelection();
            }
        }

        private bool MatchesTypeSearch(Type type) {
            if (type == null || string.IsNullOrWhiteSpace(_typeSearchText))
                return true;

            string search = _typeSearchText.Trim();
            if (type.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            MgDataKitAssetTypeEntry entry = _assetCatalog?.FindTypeEntry(type);
            return BuildTagText(entry).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string BuildTagText(MgDataKitAssetTypeEntry entry) {
            if (entry == null || _assetCatalog == null)
                return "无 Tag";

            var tagNames = new List<string>();
            IReadOnlyList<string> tagIds = entry.Tags.TagIds;
            for (var i = 0; i < tagIds.Count; i++) {
                DataTableTagDefinition definition = _assetCatalog.TagSystem.FindById(tagIds[i]);
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Name))
                    tagNames.Add(definition.Name);
            }

            return tagNames.Count == 0 ? "无 Tag" : string.Join(" / ", tagNames);
        }

        private void HandleExternalCatalogChange(EditorRefreshReason reason) {
            _allTypes = null;
            _assetCatalog = null;
            EnsureTypesLoaded();
            RefreshTypeListView();
            RefreshAssetPanel();
            NotifyExtensionsRefreshed(reason);
        }

        private void HandleUndoRedo() {
            HandleExternalCatalogChange(EditorRefreshReason.UndoRedo);
        }

        private void HandleProjectChanged() {
            HandleExternalCatalogChange(EditorRefreshReason.ProjectChanged);
        }

        private void ScheduleExternalCatalogChange() {
            EditorApplication.delayCall -= HandleProjectChanged;
            EditorApplication.delayCall += HandleProjectChanged;
        }

        public static void RepaintOpenWindows() {
            MgDataKitEditor[] windows = Resources.FindObjectsOfTypeAll<MgDataKitEditor>();
            for (var i = 0; i < windows.Length; i++)
                windows[i].HandleExternalCatalogChange(EditorRefreshReason.ExternalRequest);
            DataTableTypeConfigurationWindow.RepaintOpenWindows();
        }

        private void UpdateEditorContext() {
            if (_editorContext == null)
                return;

            _editorContext.Catalog = _assetCatalog;
            _editorContext.SelectedType = _selectedType;
            _editorContext.SelectedTypeEntry = _selectedTypeEntry;
            _editorContext.AssetEntries = _assetEntries;
            _editorContext.SelectedAsset = _assetListView?.selectedItem is MgDataKitAssetEntry selectedEntry
                ? selectedEntry.Asset
                : null;
        }

        private void RefreshAssets() {
            _assetEntries.Clear();
            _selectedTypeEntry = null;
            if (_selectedType == null) {
                UpdateEditorContext();
                RefreshAssetPanel();
                return;
            }
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out _assetCatalog,
                    out var catalogError)) {
                Debug.LogError($"[MgDataKit] Asset 目录加载失败: {catalogError}");
                RefreshAssetPanel();
                return;
            }

            _selectedTypeEntry = _assetCatalog.FindTypeEntry(_selectedType);
            if (_selectedTypeEntry != null) {
                for (var i = 0; i < _selectedTypeEntry.Assets.Count; i++) {
                    MgDataKitAssetEntry entry = _selectedTypeEntry.Assets[i];
                    if (ShouldDisplayAsset(entry))
                        _assetEntries.Add(entry);
                }
            }

            _assetListView?.Rebuild();
            UpdateEditorContext();
            RefreshAssetPanel();
        }

        private void OnAssetListItemIndexChanged(int _, int newIndex) {
            if (_assetCatalog == null || _selectedTypeEntry == null)
                return;

            List<MgDataKitAssetEntry> catalogEntries = _selectedTypeEntry.MutableAssets;
            var displayedIndices = new List<int>();
            for (var index = 0; index < catalogEntries.Count; index++) {
                if (ShouldDisplayAsset(catalogEntries[index]))
                    displayedIndices.Add(index);
            }

            if (displayedIndices.Count != _assetEntries.Count) {
                RefreshAssets();
                return;
            }

            Undo.RecordObject(_assetCatalog, "调整 MgData Asset 顺序");
            for (var index = 0; index < displayedIndices.Count; index++)
                catalogEntries[displayedIndices[index]] = _assetEntries[index];

            MgDataKitAssetCatalogProvider.Save(_assetCatalog);
            RefreshAssetPanel();
        }

        private void OnAssetSelectionChanged(IEnumerable<object> selectedItems) {
            _editorContext.SelectedAsset = selectedItems?.OfType<MgDataKitAssetEntry>()
                .Select(entry => entry.Asset)
                .FirstOrDefault();
            RefreshExtensionActions();
            NotifyExtensionsRefreshed(EditorRefreshReason.ExternalRequest);
        }

        private MgDataBase GetLatestDisplayedAsset() {
            for (var i = _assetEntries.Count - 1; i >= 0; i--) {
                if (ShouldDisplayAsset(_assetEntries[i]))
                    return _assetEntries[i].Asset;
            }

            return null;
        }

        private void ImportCurrentTypeAssets() {
            if (_selectedType == null)
                return;

            if (_assetEntries.Count == 0)
                RefreshAssets();

            var assets = _assetEntries
                .Where(entry => entry?.Asset != null)
                .Select(entry => entry.Asset)
                .ToList();

            var importedCount = 0;
            for (var i = 0; i < assets.Count; i++) {
                if (MgDataImportService.Import(assets[i]))
                    importedCount++;
            }

            Debug.Log($"[MgDataKit] 当前类型导入完成: {_selectedType.Name}, 数量={importedCount}/{assets.Count}");
            RefreshAssets();
        }

        private bool CanOpenBatchImport() {
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            return adapter is IMgDataSourceBatchImportAdapter;
        }

        private void OpenBatchImportWindow() {
            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            if (!(adapter is IMgDataSourceBatchImportAdapter batchAdapter)) {
                EditorUtility.DisplayDialog(
                    "批量导入不可用",
                    "当前数据源没有提供批量导入适配器。",
                    "确定");
                return;
            }

            if (!batchAdapter.TryOpenBatchImport(
                    _selectedType,
                    GetNewAssetDefaultFolder(),
                    out string errorMessage))
                EditorUtility.DisplayDialog("批量导入不可用", errorMessage, "确定");
        }

        private void OpenSelectedTypeConfiguration() {
            if (_selectedType != null)
                DataTableTypeConfigurationWindow.Open(_selectedType);
        }

        private void ImportAllAssets() {
            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(
                    out MgDataKitAssetCatalog catalog,
                    out string catalogError)) {
                Debug.LogError($"[MgDataKit] 全量导入失败：{catalogError}");
                return;
            }

            var allAssets = new List<MgDataBase>();
            for (var typeIndex = 0; typeIndex < catalog.Entries.Count; typeIndex++) {
                MgDataKitAssetTypeEntry typeEntry = catalog.Entries[typeIndex];
                if (typeEntry == null)
                    continue;

                for (var assetIndex = 0; assetIndex < typeEntry.Assets.Count; assetIndex++) {
                    MgDataBase asset = typeEntry.Assets[assetIndex]?.Asset;
                    if (asset != null)
                        allAssets.Add(asset);
                }
            }

            allAssets.Sort(CompareImportOrder);
            var importedCount = 0;
            for (var i = 0; i < allAssets.Count; i++) {
                if (MgDataImportService.Import(allAssets[i]))
                    importedCount++;
            }

            Debug.Log($"[MgDataKit] 全量数据源导入完成: 数量={importedCount}/{allAssets.Count}");
            RefreshAssets();
        }

        private static int CompareImportOrder(MgDataBase left, MgDataBase right) {
            int leftPriority = MgDataKitExtensionRegistry.TryGetSyncPriority(left, out int leftValue)
                ? leftValue
                : int.MaxValue;
            int rightPriority = MgDataKitExtensionRegistry.TryGetSyncPriority(right, out int rightValue)
                ? rightValue
                : int.MaxValue;
            if (leftPriority != rightPriority)
                return leftPriority.CompareTo(rightPriority);

            int typeComparison = string.Compare(
                left?.GetType().FullName,
                right?.GetType().FullName,
                StringComparison.Ordinal);
            return typeComparison != 0
                ? typeComparison
                : string.Compare(left?.name, right?.name, StringComparison.Ordinal);
        }

        private void CreateNewAsset() {
            var path = SelectNewAssetPath();
            if (string.IsNullOrEmpty(path))
                return;

            IMgDataSourceAdapter sourceAdapter = MgDataSourceAdapterRegistry.Find(_selectedTypeEntry);
            MgDataBase asset = CreateInstance(_selectedType) as MgDataBase;
            if (asset == null)
                return;

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _assetCatalog = MgDataKitAssetCatalogProvider.GetOrNull();
            MgDataKitAssetEntry entry = MgDataKitAssetCatalogProvider.RegisterAsset(asset);
            if (entry != null && sourceAdapter != null) {
                entry.SourceId = sourceAdapter.SourceId;
                if (!sourceAdapter.TryInitializeBinding(entry, out string bindingError))
                    Debug.LogWarning($"[MgDataKit] 来源绑定初始化失败：{bindingError}");
            }
            if (entry != null)
                MgDataKitAssetCatalogProvider.Save(_assetCatalog);
            RefreshAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private string SelectNewAssetPath() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/');
            var defaultFolder = GetNewAssetDefaultFolder();
            var absoluteFolder = Path.GetFullPath(Path.Combine(projectRoot, defaultFolder))
                .Replace('\\', '/');
            if (!Directory.Exists(absoluteFolder))
                absoluteFolder = Path.Combine(projectRoot, "Assets").Replace('\\', '/');

            var selectedPath = EditorUtility.SaveFilePanel(
                "新建 MgData Asset",
                absoluteFolder,
                $"New{_selectedType.Name}",
                "asset");
            if (string.IsNullOrWhiteSpace(selectedPath))
                return null;

            var fullPath = Path.GetFullPath(selectedPath).Replace('\\', '/');
            var projectPrefix = projectRoot.TrimEnd('/') + "/";
            if (!fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)) {
                EditorUtility.DisplayDialog(
                    "创建 Asset 失败",
                    "MgData Asset 必须保存到当前 Unity 项目的目录内。",
                    "确定");
                return null;
            }

            var relativePath = fullPath.Substring(projectPrefix.Length);
            if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
                EditorUtility.DisplayDialog(
                    "创建 Asset 失败",
                    "MgData Asset 必须保存到 Assets 目录内。",
                    "确定");
                return null;
            }

            if (AssetDatabase.LoadMainAssetAtPath(relativePath) != null) {
                EditorUtility.DisplayDialog(
                    "创建 Asset 失败",
                    $"目标路径已有 Asset：{relativePath}",
                    "确定");
                return null;
            }

            return relativePath;
        }

        private string GetNewAssetDefaultFolder() {
            MgDataBase latestAsset = GetLatestDisplayedAsset();
            if (latestAsset != null) {
                var latestPath = AssetDatabase.GetAssetPath(latestAsset);
                var latestDirectory = Path.GetDirectoryName(latestPath)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(latestDirectory))
                    return latestDirectory;
            }

            return "Assets/Data";
        }

        private static void RunLintValidation() {
            MgDataScriptValidationGate.ValidateAllImportedTables(true);
            if (!EditorApplication.ExecuteMenuItem("Window/General/Console"))
                EditorApplication.ExecuteMenuItem("Window/Analysis/Console");
        }

        private sealed class EditorCommandService : IMgDataKitEditorCommandService {
            private readonly MgDataKitEditor _window;

            public EditorCommandService(MgDataKitEditor window) {
                _window = window;
            }

            public void ImportAsset(MgDataKitAssetEntry entry) {
                _window.ImportAsset(entry);
            }

            public void ImportCurrentType() {
                _window.ImportCurrentTypeAssets();
            }

            public void ImportAll() {
                _window.ImportAllAssets();
            }

            public void CreateAsset() {
                _window.CreateNewAsset();
            }

            public void OpenSource(MgDataKitAssetEntry entry) {
                _window.OpenAssetSource(entry);
            }

            public void RemoveAssetReference(MgDataBase asset) {
                _window.RemoveAssetReference(asset);
            }

            public void Undo() {
                UnityEditor.Undo.PerformUndo();
            }

            public void RequestRefresh(EditorRefreshReason reason) {
                _window.HandleExternalCatalogChange(reason);
            }
        }

        private sealed class TypeListItem {
            public readonly Type Type;
            public readonly string TypeName;
            public readonly string TagText;

            public TypeListItem(Type type, string tagText) {
                Type = type;
                TypeName = type != null ? type.Name : string.Empty;
                TagText = tagText ?? "无 Tag";
            }
        }

        private sealed class AssetRowView {
            public readonly ObjectField AssetField;
            public readonly Button ImportButton;
            public readonly Button OpenSourceButton;
            public readonly Button RemoveButton;
            public readonly VisualElement ExtensionSourceContainer;
            public readonly VisualElement ExtensionActionsContainer;
            public readonly VisualElement SourceAdapterContainer;
            public readonly List<AssetRowExtensionHost> ExtensionHosts = new();
            public bool SourceAdapterBuilt;
            public string SourceAdapterId;
            public MgDataSourceAdapterContext SourceAdapterContext;
            public MgDataKitAssetEntry Entry;

            public AssetRowView(
                ObjectField assetField,
                Button importButton,
                Button openSourceButton,
                Button removeButton,
                VisualElement extensionSourceContainer,
                VisualElement extensionActionsContainer,
                VisualElement sourceAdapterContainer) {
                AssetField = assetField;
                ImportButton = importButton;
                OpenSourceButton = openSourceButton;
                RemoveButton = removeButton;
                ExtensionSourceContainer = extensionSourceContainer;
                ExtensionActionsContainer = extensionActionsContainer;
                SourceAdapterContainer = sourceAdapterContainer;
            }
        }

        private sealed class AssetRowExtensionHost {
            public readonly IMgDataKitAssetRowExtension Extension;
            public readonly VisualElement SourceContainer;
            public readonly VisualElement ActionsContainer;
            public MgDataKitAssetRowContext Context;

            public AssetRowExtensionHost(
                IMgDataKitAssetRowExtension extension,
                VisualElement sourceContainer,
                VisualElement actionsContainer) {
                Extension = extension;
                SourceContainer = sourceContainer;
                ActionsContainer = actionsContainer;
            }
        }

    }
}
