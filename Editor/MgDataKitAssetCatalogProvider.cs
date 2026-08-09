#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MgDataKit;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [InitializeOnLoad]
    internal static class MgDataKitAssetCatalogProvider {
        public const string AssetPath = "Assets/MgDataKit/Project/MgDataKitAssetCatalog.asset";

        private static MgDataKitAssetCatalog _cachedInstance;
        private static List<string> _cachedPaths;
        private static MgDataKitAssetCatalog _preparedCatalog;
        private static bool _catalogPreparationComplete;

        static MgDataKitAssetCatalogProvider() {
            EditorApplication.projectChanged += InvalidateCache;
        }

        public static MgDataKitAssetCatalog GetOrNull() {
            return TryGet(out MgDataKitAssetCatalog catalog, out _) ? catalog : null;
        }

        public static bool TryGet(out MgDataKitAssetCatalog catalog, out string errorMessage) {
            RefreshCacheIfNeeded();
            catalog = null;
            errorMessage = null;

            if (_cachedPaths.Count == 0) {
                errorMessage = "未找到 MgDataKitAssetCatalog。";
                return false;
            }

            if (_cachedPaths.Count > 1) {
                errorMessage = "检测到多个 MgDataKitAssetCatalog，项目中只能保留一个：\n" +
                               string.Join("\n", _cachedPaths);
                return false;
            }

            if (_cachedInstance == null)
                _cachedInstance = AssetDatabase.LoadAssetAtPath<MgDataKitAssetCatalog>(_cachedPaths[0]);

            if (_cachedInstance != null) {
                catalog = _cachedInstance;
                return true;
            }

            errorMessage = $"无法加载 MgDataKitAssetCatalog：{_cachedPaths[0]}";
            return false;
        }

        public static bool TryGetOrCreate(out MgDataKitAssetCatalog catalog, out string errorMessage) {
            if (TryGet(out catalog, out errorMessage))
                return true;
            if (_cachedPaths.Count > 0)
                return false;
            return TryCreate(out catalog, out errorMessage);
        }

        public static IReadOnlyList<string> GetAllAssetPaths() {
            RefreshCacheIfNeeded();
            return _cachedPaths;
        }

        public static bool TryCreate(out MgDataKitAssetCatalog catalog, out string errorMessage) {
            catalog = null;
            errorMessage = null;
            RefreshCacheIfNeeded();
            if (_cachedPaths.Count > 0) {
                errorMessage = _cachedPaths.Count == 1
                    ? $"项目中已存在 MgDataKitAssetCatalog：{_cachedPaths[0]}"
                    : "项目中存在多个 MgDataKitAssetCatalog，请先处理重复实例。";
                return false;
            }

            if (AssetDatabase.LoadMainAssetAtPath(AssetPath) != null) {
                errorMessage = $"目标位置已有其他资产：{AssetPath}";
                return false;
            }

            EnsureAssetFolder(Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<MgDataKitAssetCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
            AssetDatabase.SaveAssetIfDirty(catalog);
            InvalidateCache();
            TryGet(out catalog, out _);
            return catalog != null;
        }

        // MgDataBase membership is explicit. Preparation never discovers or registers Asset instances.
        public static bool TryEnsureCatalogReady(
            out MgDataKitAssetCatalog catalog,
            out string errorMessage) {
            if (!TryGetOrCreate(out catalog, out errorMessage))
                return false;

            if (_catalogPreparationComplete && _preparedCatalog == catalog)
                return true;

            var changed = catalog.TagSystem.Normalize();
            var knownAssets = new HashSet<int>();
            var knownTypes = new Dictionary<Type, MgDataKitAssetTypeEntry>();
            List<MgDataKitAssetTypeEntry> typeEntries = catalog.MutableTypeEntries;
            for (var typeIndex = 0; typeIndex < typeEntries.Count;) {
                MgDataKitAssetTypeEntry typeEntry = typeEntries[typeIndex];
                Type assetType = typeEntry?.AssetType;
                if (!IsConcreteDataType(assetType)) {
                    typeEntries.RemoveAt(typeIndex);
                    changed = true;
                    continue;
                }

                if (knownTypes.TryGetValue(assetType, out MgDataKitAssetTypeEntry existingTypeEntry)) {
                    MergeTypeEntries(existingTypeEntry, typeEntry);
                    typeEntries.RemoveAt(typeIndex);
                    changed = true;
                    continue;
                }

                knownTypes.Add(assetType, typeEntry);
                changed |= typeEntry.Tags.Prune(catalog.TagSystem);
                List<MgDataKitAssetEntry> entries = typeEntry.MutableAssets;
                for (var entryIndex = 0; entryIndex < entries.Count;) {
                    MgDataKitAssetEntry entry = entries[entryIndex];
                    if (entry == null || entry.Asset == null || entry.Asset.GetType() != assetType ||
                        !knownAssets.Add(entry.Asset.GetInstanceID())) {
                        entries.RemoveAt(entryIndex);
                        changed = true;
                        continue;
                    }

                    entryIndex++;
                }

                typeIndex++;
            }

            List<Type> concreteTypes = FindAllConcreteDataTypes();
            for (var i = 0; i < concreteTypes.Count; i++) {
                Type assetType = concreteTypes[i];
                if (knownTypes.ContainsKey(assetType))
                    continue;

                MonoScript typeScript = FindMonoScript(assetType);
                MgDataKitAssetTypeEntry typeEntry = catalog.AddTypeEntry(typeScript);
                if (typeEntry == null) {
                    Debug.LogWarning($"[MgDataKit] 无法为 {assetType.FullName} 找到 MonoScript，未创建类型配置。");
                    continue;
                }

                knownTypes.Add(assetType, typeEntry);
                changed = true;
            }

            if (changed)
                Save(catalog);

            _preparedCatalog = catalog;
            _catalogPreparationComplete = true;
            return true;
        }

        public static List<MgDataKitAssetEntry> GetEntries(Type assetType) {
            var result = new List<MgDataKitAssetEntry>();
            if (!TryGetTypeEntry(assetType, out MgDataKitAssetTypeEntry typeEntry))
                return result;

            result.AddRange(typeEntry.Assets);
            return result;
        }

        public static bool TryGetTypeEntry(Type assetType, out MgDataKitAssetTypeEntry typeEntry) {
            typeEntry = null;
            if (assetType == null || !TryEnsureCatalogReady(out MgDataKitAssetCatalog catalog, out _))
                return false;

            typeEntry = catalog.FindTypeEntry(assetType);
            return typeEntry != null;
        }

        public static MgDataKitAssetTypeEntry GetTypeEntry(Type assetType) {
            return TryGetTypeEntry(assetType, out MgDataKitAssetTypeEntry typeEntry) ? typeEntry : null;
        }

        public static string GetSourceId(Type assetType) {
            MgDataKitAssetTypeEntry typeEntry = GetTypeEntry(assetType);
            return typeEntry?.SourceId ?? string.Empty;
        }

        public static IMgDataSourceAdapter GetSourceAdapter(Type assetType) {
            return MgDataSourceAdapterRegistry.Find(GetTypeEntry(assetType));
        }

        /// <summary>
        /// Validate the source fields stored in a Catalog entry.
        /// Keeping this in the Catalog layer gives import and lint the same exclusivity rules.
        /// </summary>
        internal static bool TryValidateSourceBinding(
            MgDataKitAssetEntry entry,
            MgDataKitAssetTypeEntry typeEntry,
            out string errorMessage) {
            errorMessage = null;
            if (entry == null || entry.Asset == null) {
                errorMessage = "MgData Catalog Entry 为空。";
                return false;
            }

            if (typeEntry == null) {
                errorMessage = $"未找到 {entry.Asset.GetType().Name} 的 Catalog 类型配置。";
                return false;
            }

            IMgDataSourceAdapter adapter = MgDataSourceAdapterRegistry.Find(typeEntry);
            if (adapter == null) {
                errorMessage = $"未找到来源适配器：{typeEntry.SourceId}";
                return false;
            }

            return adapter.TryValidate(entry, out errorMessage);
        }

        public static bool HasTag(Type assetType, string tagName) {
            if (!TryGetTypeEntry(assetType, out MgDataKitAssetTypeEntry typeEntry) ||
                !TryGet(out MgDataKitAssetCatalog catalog, out _))
                return false;

            DataTableTagDefinition definition = catalog.TagSystem.FindByName(tagName);
            return definition != null && typeEntry.Tags.Contains(definition.Id);
        }

        public static bool HasTag(Type assetType, DataTableTagDefinition definition) {
            return definition != null &&
                   TryGetTypeEntry(assetType, out MgDataKitAssetTypeEntry typeEntry) &&
                   typeEntry.Tags.Contains(definition.Id);
        }

        public static MgDataKitAssetEntry RegisterAsset(MgDataBase asset) {
            if (asset == null || !TryGetOrCreate(out MgDataKitAssetCatalog catalog, out _))
                return null;

            MgDataKitAssetEntry entry = catalog.FindEntry(asset) ?? catalog.AddEntry(asset);
            if (entry == null)
                return null;

            Save(catalog);
            return entry;
        }

        public static bool TryGetEntry(MgDataBase asset, out MgDataKitAssetEntry entry) {
            entry = null;
            if (asset == null || !TryGet(out MgDataKitAssetCatalog catalog, out _))
                return false;

            entry = catalog.FindEntry(asset);
            return entry != null;
        }

        public static bool RemoveAssetReference(MgDataBase asset) {
            if (asset == null || !TryGet(out MgDataKitAssetCatalog catalog, out _))
                return false;

            if (!catalog.RemoveEntry(asset))
                return false;

            Save(catalog);
            return true;
        }

        public static void Save(MgDataKitAssetCatalog catalog) {
            if (catalog == null)
                return;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        public static void InvalidateCache() {
            _cachedInstance = null;
            _cachedPaths = null;
            _preparedCatalog = null;
            _catalogPreparationComplete = false;
        }

        internal static List<Type> FindAllConcreteDataTypes() {
            var result = new List<Type>();
            TypeCache.TypeCollection types = TypeCache.GetTypesDerivedFrom<MgDataBase>();
            for (var i = 0; i < types.Count; i++) {
                Type type = types[i];
                if (IsConcreteDataType(type))
                    result.Add(type);
            }

            result.Sort((left, right) => string.Compare(
                left.FullName,
                right.FullName,
                StringComparison.Ordinal));
            return result;
        }

        internal static MonoScript FindMonoScript(Type assetType) {
            if (assetType == null)
                return null;

            MonoScript[] scripts = MonoImporter.GetAllRuntimeMonoScripts();
            for (var i = 0; i < scripts.Length; i++) {
                MonoScript script = scripts[i];
                if (script != null && script.GetClass() == assetType)
                    return script;
            }

            return null;
        }

        private static void MergeTypeEntries(
            MgDataKitAssetTypeEntry destination,
            MgDataKitAssetTypeEntry source) {
            if (destination == null || source == null)
                return;

            IReadOnlyList<string> sourceTagIds = source.Tags.TagIds;
            for (var i = 0; i < sourceTagIds.Count; i++)
                destination.Tags.Add(sourceTagIds[i]);

            List<MgDataKitAssetEntry> sourceAssets = source.MutableAssets;
            for (var i = 0; i < sourceAssets.Count; i++) {
                MgDataKitAssetEntry sourceEntry = sourceAssets[i];
                if (sourceEntry?.Asset != null && destination.FindEntry(sourceEntry.Asset) == null)
                    destination.MutableAssets.Add(sourceEntry);
            }
        }

        private static bool IsConcreteDataType(Type type) {
            return type != null && !type.IsAbstract && typeof(MgDataBase).IsAssignableFrom(type);
        }

        private static void RefreshCacheIfNeeded() {
            if (_cachedPaths != null)
                return;

            _cachedPaths = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:MgDataKitAssetCatalog");
            for (var i = 0; i < guids.Length; i++) {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path))
                    _cachedPaths.Add(path);
            }

            _cachedPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureAssetFolder(string folderPath) {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++) {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

    }
}
#endif
