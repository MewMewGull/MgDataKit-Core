#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MgDataKit.Editor {
    internal sealed class MgDataKitEditorExtensionRegistry : IMgDataKitEditorRegistry {
        private readonly Dictionary<MgDataKitEditorActionSlot, List<MgDataKitEditorActionDefinition>> _actions =
            new();
        private readonly List<IMgDataKitAssetRowExtension> _assetRowExtensions = new();
        private readonly Dictionary<MgDataKitEditorActionSlot, List<IMgDataKitEditorViewExtension>> _views =
            new();
        private readonly List<IMgDataKitEditorLifecycleExtension> _lifecycleExtensions = new();
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public static MgDataKitEditorExtensionRegistry Discover() {
            var registry = new MgDataKitEditorExtensionRegistry();
            List<IMgDataKitEditorExtension> extensions =
                DiscoverInstances<IMgDataKitEditorExtension>().ToList();
            extensions.Sort(CompareExtensions);
            for (var i = 0; i < extensions.Count; i++) {
                IMgDataKitEditorExtension extension = extensions[i];
                if (extension == null || !registry._ids.Add(extension.Id ?? string.Empty)) {
                    Debug.LogError($"[MgDataKit] 重复或无效的 Editor 扩展 ID：{extension?.Id}");
                    continue;
                }

                try {
                    extension.Register(registry);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] Editor 扩展注册失败：{extension.Id}\n{ex}");
                }
            }

            registry.Sort();
            return registry;
        }

        private static int CompareExtensions(
            IMgDataKitEditorExtension left,
            IMgDataKitEditorExtension right) {
            int orderComparison = (left?.Order ?? int.MaxValue).CompareTo(right?.Order ?? int.MaxValue);
            if (orderComparison != 0)
                return orderComparison;

            int idComparison = string.CompareOrdinal(left?.Id, right?.Id);
            return idComparison != 0
                ? idComparison
                : string.CompareOrdinal(left?.GetType().FullName, right?.GetType().FullName);
        }

        public void RegisterAction(
            MgDataKitEditorActionSlot slot,
            MgDataKitEditorActionDefinition definition) {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("Editor Action 必须提供稳定 ID。", nameof(definition));

            if (!_actions.TryGetValue(slot, out List<MgDataKitEditorActionDefinition> definitions)) {
                definitions = new List<MgDataKitEditorActionDefinition>();
                _actions.Add(slot, definitions);
            }

            if (definitions.Any(item => string.Equals(item.Id, definition.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Editor Action ID 重复：{definition.Id}");

            definitions.Add(definition);
        }

        public void RegisterAssetRowExtension(IMgDataKitAssetRowExtension extension) {
            if (extension == null || string.IsNullOrWhiteSpace(extension.Id))
                throw new ArgumentException("Asset 行扩展必须提供稳定 ID。", nameof(extension));
            if (_assetRowExtensions.Any(item => string.Equals(item.Id, extension.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Asset 行扩展 ID 重复：{extension.Id}");

            _assetRowExtensions.Add(extension);
        }

        public void RegisterView(
            MgDataKitEditorActionSlot slot,
            IMgDataKitEditorViewExtension extension) {
            if (extension == null || string.IsNullOrWhiteSpace(extension.Id))
                throw new ArgumentException("Editor 视图扩展必须提供稳定 ID。", nameof(extension));
            if (!_views.TryGetValue(slot, out List<IMgDataKitEditorViewExtension> extensions)) {
                extensions = new List<IMgDataKitEditorViewExtension>();
                _views.Add(slot, extensions);
            }
            if (extensions.Any(item => string.Equals(item.Id, extension.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Editor 视图扩展 ID 重复：{extension.Id}");

            extensions.Add(extension);
        }

        public void RegisterLifecycle(IMgDataKitEditorLifecycleExtension extension) {
            if (extension == null || string.IsNullOrWhiteSpace(extension.Id))
                throw new ArgumentException("Editor 生命周期扩展必须提供稳定 ID。", nameof(extension));
            if (_lifecycleExtensions.Any(item => string.Equals(item.Id, extension.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Editor 生命周期扩展 ID 重复：{extension.Id}");

            _lifecycleExtensions.Add(extension);
        }

        public IReadOnlyList<MgDataKitEditorActionDefinition> GetActions(MgDataKitEditorActionSlot slot) {
            return _actions.TryGetValue(slot, out List<MgDataKitEditorActionDefinition> definitions)
                ? definitions
                : Array.Empty<MgDataKitEditorActionDefinition>();
        }

        public IReadOnlyList<IMgDataKitAssetRowExtension> AssetRowExtensions => _assetRowExtensions;
        public IReadOnlyList<IMgDataKitEditorLifecycleExtension> LifecycleExtensions => _lifecycleExtensions;

        public IReadOnlyList<IMgDataKitEditorViewExtension> GetViews(MgDataKitEditorActionSlot slot) {
            return _views.TryGetValue(slot, out List<IMgDataKitEditorViewExtension> extensions)
                ? extensions
                : Array.Empty<IMgDataKitEditorViewExtension>();
        }

        private void Sort() {
            foreach (List<MgDataKitEditorActionDefinition> definitions in _actions.Values)
                definitions.Sort((left, right) => Compare(left.Order, left.Id, right.Order, right.Id));
            foreach (List<IMgDataKitEditorViewExtension> extensions in _views.Values)
                extensions.Sort((left, right) => Compare(left.Order, left.Id, right.Order, right.Id));
            _assetRowExtensions.Sort((left, right) => Compare(left.Order, left.Id, right.Order, right.Id));
            _lifecycleExtensions.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        }

        private static int Compare(int leftOrder, string leftId, int rightOrder, string rightId) {
            int orderComparison = leftOrder.CompareTo(rightOrder);
            return orderComparison != 0
                ? orderComparison
                : string.CompareOrdinal(leftId, rightId);
        }

        private static IReadOnlyList<T> DiscoverInstances<T>() where T : class {
            var types = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++) {
                Type[] assemblyTypes;
                try {
                    assemblyTypes = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex) {
                    assemblyTypes = ex.Types ?? Array.Empty<Type>();
                }

                for (var j = 0; j < assemblyTypes.Length; j++) {
                    Type type = assemblyTypes[j];
                    if (type == null || type.IsAbstract || type.IsInterface || !typeof(T).IsAssignableFrom(type))
                        continue;
                    types.Add(type);
                }
            }

            types.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            var instances = new List<T>();
            for (var i = 0; i < types.Count; i++) {
                try {
                    if (Activator.CreateInstance(types[i]) is T instance)
                        instances.Add(instance);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] 无法创建 Editor 扩展 {types[i].FullName}：{ex.Message}");
                }
            }

            return instances;
        }
    }

    internal static class MgDataSourceAdapterRegistry {
        private static IReadOnlyList<IMgDataSourceAdapter> _adapters;

        public static IReadOnlyList<IMgDataSourceAdapter> GetAll() {
            if (_adapters == null)
                _adapters = Discover();
            return _adapters;
        }

        public static IMgDataSourceAdapter Find(MgDataKitAssetTypeEntry typeEntry) {
            IReadOnlyList<IMgDataSourceAdapter> adapters = GetAll();
            for (var i = 0; i < adapters.Count; i++) {
                if (adapters[i].CanHandle(typeEntry))
                    return adapters[i];
            }

            return null;
        }

        public static IMgDataSourceAdapter Find(string sourceId) {
            if (string.IsNullOrWhiteSpace(sourceId))
                return null;

            IReadOnlyList<IMgDataSourceAdapter> adapters = GetAll();
            for (var i = 0; i < adapters.Count; i++) {
                if (string.Equals(adapters[i].SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                    return adapters[i];
            }

            return null;
        }

        private static IReadOnlyList<IMgDataSourceAdapter> Discover() {
            var result = new List<IMgDataSourceAdapter>();
            var typeList = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++) {
                Type[] types;
                try {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex) {
                    types = ex.Types ?? Array.Empty<Type>();
                }

                for (var j = 0; j < types.Length; j++) {
                    Type type = types[j];
                    if (type == null || type.IsAbstract || type.IsInterface || !typeof(IMgDataSourceAdapter).IsAssignableFrom(type))
                        continue;
                    typeList.Add(type);
                }
            }

            typeList.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < typeList.Count; i++) {
                try {
                    if (!(Activator.CreateInstance(typeList[i]) is IMgDataSourceAdapter adapter))
                        continue;
                    if (string.IsNullOrWhiteSpace(adapter.SourceId)) {
                        Debug.LogError($"[MgDataKit] 数据源适配器缺少 SourceId：{typeList[i].FullName}");
                        continue;
                    }
                    if (!ids.Add(adapter.SourceId)) {
                        Debug.LogError($"[MgDataKit] 重复的数据源适配器 ID：{adapter.SourceId}");
                        continue;
                    }

                    result.Add(adapter);
                }
                catch (Exception ex) {
                    Debug.LogError($"[MgDataKit] 无法创建数据源适配器 {typeList[i].FullName}：{ex.Message}");
                }
            }

            result.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return result;
        }
    }
}

#endif
