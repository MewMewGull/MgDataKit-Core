#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MgDataKit;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    public sealed class MgDataKitAssetEntry {
        [SerializeField]
        private MgDataBase _asset;

        [SerializeField]
        private string _sourceId;

        [SerializeField]
        [TextArea]
        private string _sourceData;

        public MgDataBase Asset => _asset;
        public string SourceId {
            get => _sourceId ?? string.Empty;
            set => _sourceId = value ?? string.Empty;
        }

        public string SourceData {
            get => _sourceData ?? string.Empty;
            set => _sourceData = value ?? string.Empty;
        }

        internal bool HasSourceId => !string.IsNullOrWhiteSpace(_sourceId);
        internal MgDataKitAssetEntry(MgDataBase asset) {
            _asset = asset;
        }

        private MgDataKitAssetEntry() {
        }
    }

    [Serializable]
    public sealed class MgDataKitAssetTypeEntry {
        [SerializeField]
        private MonoScript _typeScript;

        [SerializeField]
        private string _sourceId;

        [SerializeField]
        private DataTableTagContainer _tags = new();

        [SerializeField]
        private List<MgDataKitAssetEntry> _assets = new();

        public MonoScript TypeScript => _typeScript;
        public Type AssetType => _typeScript != null ? _typeScript.GetClass() : null;
        public string SourceId {
            get => _sourceId ?? string.Empty;
            set => _sourceId = value ?? string.Empty;
        }

        internal bool HasSourceId => !string.IsNullOrWhiteSpace(_sourceId);

        public DataTableTagContainer Tags => _tags ??= new DataTableTagContainer();
        public IReadOnlyList<MgDataKitAssetEntry> Assets => MutableAssets;

        internal List<MgDataKitAssetEntry> MutableAssets =>
            _assets ??= new List<MgDataKitAssetEntry>();

        internal MgDataKitAssetTypeEntry(MgDataBase asset)
            : this(asset != null ? MonoScript.FromScriptableObject(asset) : null) {
        }

        internal MgDataKitAssetTypeEntry(MonoScript typeScript) {
            _typeScript = typeScript;
        }

        private MgDataKitAssetTypeEntry() {
        }

        internal bool Matches(Type assetType) {
            return assetType != null && AssetType == assetType;
        }

        internal void SetTypeScript(MonoScript typeScript) {
            _typeScript = typeScript;
        }

        internal MgDataKitAssetEntry FindEntry(MgDataBase asset) {
            if (asset == null)
                return null;

            List<MgDataKitAssetEntry> assets = MutableAssets;
            for (var i = 0; i < assets.Count; i++) {
                if (assets[i] != null && assets[i].Asset == asset)
                    return assets[i];
            }

            return null;
        }

        internal MgDataKitAssetEntry AddEntry(MgDataBase asset) {
            if (asset == null)
                return null;

            MgDataKitAssetEntry existing = FindEntry(asset);
            if (existing != null)
                return existing;

            var entry = new MgDataKitAssetEntry(asset) {
                SourceId = SourceId,
                SourceData = string.Empty
            };
            MutableAssets.Add(entry);
            return entry;
        }
    }

    /// <summary>
    /// MgDataKit 项目级 Asset 目录，按 MgDataBase 类型保存受管理 Asset、源和显示顺序。
    /// </summary>
    public sealed class MgDataKitAssetCatalog : ScriptableObject {
        [SerializeField]
        private List<MgDataKitAssetTypeEntry> _typeEntries = new();

        [SerializeField]
        private DataTableTagSystem _tagSystem = new();

        public IReadOnlyList<MgDataKitAssetTypeEntry> Entries => MutableTypeEntries;
        public DataTableTagSystem TagSystem => _tagSystem ??= new DataTableTagSystem();

        internal List<MgDataKitAssetTypeEntry> MutableTypeEntries =>
            _typeEntries ??= new List<MgDataKitAssetTypeEntry>();

        internal MgDataKitAssetTypeEntry FindTypeEntry(Type assetType) {
            if (assetType == null)
                return null;

            List<MgDataKitAssetTypeEntry> typeEntries = MutableTypeEntries;
            for (var i = 0; i < typeEntries.Count; i++) {
                if (typeEntries[i] != null && typeEntries[i].Matches(assetType))
                    return typeEntries[i];
            }

            return null;
        }

        internal MgDataKitAssetEntry FindEntry(MgDataBase asset) {
            if (asset == null)
                return null;

            MgDataKitAssetTypeEntry typeEntry = FindTypeEntry(asset.GetType());
            return typeEntry?.FindEntry(asset);
        }

        internal MgDataKitAssetEntry AddEntry(MgDataBase asset) {
            if (asset == null)
                return null;

            MgDataKitAssetTypeEntry typeEntry = FindTypeEntry(asset.GetType());
            if (typeEntry == null) {
                typeEntry = new MgDataKitAssetTypeEntry(asset);
                if (typeEntry.TypeScript == null) {
                    Debug.LogError($"[MgDataKit] 无法解析 {asset.GetType().FullName} 的 MonoScript。");
                    return null;
                }
                MutableTypeEntries.Add(typeEntry);
            }

            return typeEntry.AddEntry(asset);
        }

        internal MgDataKitAssetTypeEntry AddTypeEntry(MonoScript typeScript) {
            Type assetType = typeScript != null ? typeScript.GetClass() : null;
            if (assetType == null || assetType.IsAbstract || !typeof(MgDataBase).IsAssignableFrom(assetType))
                return null;

            MgDataKitAssetTypeEntry existing = FindTypeEntry(assetType);
            if (existing != null)
                return existing;

            var typeEntry = new MgDataKitAssetTypeEntry(typeScript);
            MutableTypeEntries.Add(typeEntry);
            return typeEntry;
        }

        internal bool RemoveEntry(MgDataBase asset) {
            if (asset == null)
                return false;

            MgDataKitAssetTypeEntry typeEntry = FindTypeEntry(asset.GetType());
            if (typeEntry == null)
                return false;

            MgDataKitAssetEntry entry = typeEntry.FindEntry(asset);
            if (entry == null)
                return false;

            typeEntry.MutableAssets.Remove(entry);
            return true;
        }
    }
}
#endif
