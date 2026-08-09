#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    public enum EditorRefreshReason {
        InitialLayout,
        TypeSelectionChanged,
        CatalogChanged,
        ProjectChanged,
        UndoRedo,
        ExternalRequest
    }

    public enum MgDataKitEditorActionSlot {
        LeftPaneActions,
        AssetPaneActions,
        AssetEmptyState,
        AssetRowSource,
        AssetRowActions
    }

    public sealed class MgDataKitEditorContext {
        public MgDataKitAssetCatalog Catalog { get; internal set; }
        public Type SelectedType { get; internal set; }
        public MgDataKitAssetTypeEntry SelectedTypeEntry { get; internal set; }
        public IReadOnlyList<MgDataKitAssetEntry> AssetEntries { get; internal set; }
        public MgDataBase SelectedAsset { get; internal set; }
        public EditorRefreshReason LastRefreshReason { get; internal set; }
    }

    public sealed class MgDataKitAssetRowContext {
        public MgDataKitEditorContext Editor { get; internal set; }
        public MgDataKitAssetEntry Entry { get; internal set; }
        public MgDataBase Asset => Entry?.Asset;
        public VisualElement Row { get; internal set; }
        public VisualElement SourceContainer { get; internal set; }
        public VisualElement ActionsContainer { get; internal set; }
    }

    public interface IMgDataKitEditorCommandService {
        void ImportAsset(MgDataKitAssetEntry entry);
        void ImportCurrentType();
        void ImportAll();
        void CreateAsset();
        void OpenSource(MgDataKitAssetEntry entry);
        void RemoveAssetReference(MgDataBase asset);
        void Undo();
        void RequestRefresh(EditorRefreshReason reason);
    }

    public interface IMgDataKitEditorExtension {
        string Id { get; }
        int Order { get; }
        void Register(IMgDataKitEditorRegistry registry);
    }

    public interface IMgDataKitEditorRegistry {
        void RegisterAction(
            MgDataKitEditorActionSlot slot,
            MgDataKitEditorActionDefinition definition);

        void RegisterAssetRowExtension(IMgDataKitAssetRowExtension extension);

        void RegisterView(
            MgDataKitEditorActionSlot slot,
            IMgDataKitEditorViewExtension extension);

        void RegisterLifecycle(IMgDataKitEditorLifecycleExtension extension);
    }

    public interface IMgDataKitAssetRowExtension {
        string Id { get; }
        int Order { get; }
        bool IsVisible(MgDataKitAssetRowContext context);
        void Build(MgDataKitAssetRowContext context);
        void Bind(MgDataKitAssetRowContext context);
    }

    public interface IMgDataKitEditorViewExtension {
        string Id { get; }
        int Order { get; }
        bool IsVisible(MgDataKitEditorContext context);
        void Build(MgDataKitEditorContext context, VisualElement container);
        void Refresh(MgDataKitEditorContext context, VisualElement container);
    }

    public interface IMgDataKitEditorLifecycleExtension {
        string Id { get; }
        void OnWindowCreated(MgDataKitEditorContext context);
        void OnRefresh(MgDataKitEditorContext context, EditorRefreshReason reason);
        void OnWindowDestroyed(MgDataKitEditorContext context);
    }

    public sealed class MgDataKitEditorActionDefinition {
        public string Id { get; }
        public string Text { get; }
        public string Tooltip { get; }
        public int Order { get; }
        public Func<MgDataKitEditorContext, bool> IsVisible { get; }
        public Func<MgDataKitEditorContext, bool> IsEnabled { get; }
        public Action<MgDataKitEditorContext, IMgDataKitEditorCommandService> Execute { get; }

        public MgDataKitEditorActionDefinition(
            string id,
            string text,
            string tooltip,
            int order,
            Action<MgDataKitEditorContext, IMgDataKitEditorCommandService> execute,
            Func<MgDataKitEditorContext, bool> isVisible = null,
            Func<MgDataKitEditorContext, bool> isEnabled = null) {
            Id = id ?? string.Empty;
            Text = text ?? string.Empty;
            Tooltip = tooltip ?? string.Empty;
            Order = order;
            Execute = execute;
            IsVisible = isVisible;
            IsEnabled = isEnabled;
        }
    }

    public sealed class MgDataKitEditorActionHandle : IDisposable {
        private readonly Action _dispose;
        private bool _disposed;

        internal MgDataKitEditorActionHandle(Action dispose) {
            _dispose = dispose;
        }

        public void Dispose() {
            if (_disposed)
                return;

            _disposed = true;
            _dispose?.Invoke();
        }
    }

    public sealed class MgDataSourceAdapterContext {
        public MgDataKitEditorContext Editor { get; internal set; }
        public IMgDataKitEditorCommandService Commands { get; internal set; }
        public MgDataKitAssetRowContext Row { get; internal set; }
        public MgDataKitAssetEntry Entry => Row?.Entry;
        public MgDataBase Asset => Row?.Asset;
    }

    public interface IMgDataSourceAdapter {
        string SourceId { get; }
        string DisplayName { get; }
        bool CanHandle(MgDataKitAssetTypeEntry typeEntry);
        bool TryValidate(MgDataKitAssetEntry entry, out string errorMessage);
        MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry);
        bool TryInitializeBinding(MgDataKitAssetEntry entry, out string errorMessage);
        void BuildBindingUI(MgDataSourceAdapterContext context, VisualElement container);
        void BindBindingUI(MgDataSourceAdapterContext context, VisualElement container);
        bool TryOpenSource(MgDataKitAssetEntry entry, out string errorMessage);
    }

    public interface IMgDataSourceBatchImportAdapter {
        bool TryOpenBatchImport(Type assetType, string defaultOutputFolder, out string errorMessage);
    }

    public interface IMgDataSourceAutoImportAdapter {
        bool CanHandleAssetChange(string path);
        bool TryGetSourcePath(MgDataKitAssetEntry entry, out string fullPath);
    }

    public interface IMgDataPlayModeSyncProvider {
        bool TrySyncBeforePlay(out string errorMessage);
    }

    public interface IMgDataRowReferenceProvider {
        bool CanHandle(MgDataBase asset);
        List<string> Build(MgDataBase asset, int rowCount, string listFieldName);
    }
}

#endif
