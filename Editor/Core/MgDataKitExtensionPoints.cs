#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using MgDataKit;

namespace MgDataKit.Editor {
    /// <summary>
    /// Allows a project or an optional package to preserve table-specific import contracts.
    /// The core importer does not need to know concrete table types.
    /// </summary>
    public interface IMgDataImportExtension {
        bool CanHandle(MgDataBase asset);

        object CaptureBeforeImport(MgDataBase asset);

        bool TryApply(
            MgDataBase asset,
            object snapshot,
            string sourceLabel,
            out string errorMessage);
    }

    /// <summary>
    /// Converts a configured data source into MgDataKit's common string grid.
    /// </summary>
    public interface IMgDataSourceImporter {
        bool CanImport(string sourceId);

        MgDataSourceReadResult Read(MgDataBase asset, MgDataKitAssetEntry entry);
    }

    public sealed class MgDataSourceReadResult {
        public bool Success;
        public string[][] Grid;
        public string SourceLabel;
        public string SheetName;
        public string SheetId;
        public string ErrorMessage;

        public static MgDataSourceReadResult Failed(string message) {
            return new MgDataSourceReadResult {
                Success = false,
                ErrorMessage = message ?? "数据源读取失败。"
            };
        }
    }

    /// <summary>
    /// Provides optional ordering for related data-source synchronization.
    /// Lower values are synchronized first.
    /// </summary>
    public interface IMgDataSyncOrderProvider {
        bool TryGetPriority(MgDataBase asset, out int priority);
    }

    /// <summary>
    /// Lets an optional data-source package map source text to a project-specific field type.
    /// Core keeps the built-in scalar conversions and delegates external asset references here.
    /// </summary>
    public interface IMgDataValueConverter {
        bool CanConvert(Type targetType);

        bool IsKnownTypeName(string typeName);

        bool TryConvert(
            string raw,
            Type targetType,
            Type rowType,
            out object value,
            out string errorMessage);
    }

    internal sealed class MgDataImportExtensionState {
        public IMgDataImportExtension Extension;
        public object Snapshot;
    }

    public static class MgDataKitExtensionRegistry {
        private static IReadOnlyList<IMgDataImportExtension> _importExtensions;
        private static IReadOnlyList<IMgDataSourceImporter> _dataSourceImporters;
        private static IReadOnlyList<IMgDataSyncOrderProvider> _syncOrderProviders;
        private static IReadOnlyList<IMgDataValueConverter> _valueConverters;
        private static IReadOnlyList<IMgDataPlayModeSyncProvider> _playModeSyncProviders;
        private static IReadOnlyList<IMgDataRowReferenceProvider> _rowReferenceProviders;

        public static IReadOnlyList<IMgDataImportExtension> GetImportExtensions(MgDataBase asset) {
            var result = new List<IMgDataImportExtension>();
            if (asset == null)
                return result;

            IReadOnlyList<IMgDataImportExtension> extensions = GetImportExtensions();
            for (var i = 0; i < extensions.Count; i++) {
                if (extensions[i].CanHandle(asset))
                    result.Add(extensions[i]);
            }

            return result;
        }

        public static bool TryGetSyncPriority(MgDataBase asset, out int priority) {
            priority = int.MaxValue;
            bool found = false;
            IReadOnlyList<IMgDataSyncOrderProvider> providers = GetSyncOrderProviders();
            for (var i = 0; i < providers.Count; i++) {
                if (providers[i].TryGetPriority(asset, out int candidate) && candidate < priority) {
                    priority = candidate;
                    found = true;
                }
            }

            return found;
        }

        public static IMgDataSourceImporter GetDataSourceImporter(string sourceId) {
            if (_dataSourceImporters == null)
                _dataSourceImporters = Discover<IMgDataSourceImporter>();

            IReadOnlyList<IMgDataSourceImporter> importers = _dataSourceImporters;
            for (var i = 0; i < importers.Count; i++) {
                if (importers[i].CanImport(sourceId))
                    return importers[i];
            }

            return null;
        }

        public static bool IsKnownValueTypeName(string typeName) {
            if (MgDataValueParser.IsKnownTypeName(typeName))
                return true;

            IReadOnlyList<IMgDataValueConverter> converters = GetValueConverters();
            for (var i = 0; i < converters.Count; i++) {
                if (converters[i].IsKnownTypeName(typeName))
                    return true;
            }

            return false;
        }

        public static bool CanConvertValue(Type targetType) {
            IReadOnlyList<IMgDataValueConverter> converters = GetValueConverters();
            for (var i = 0; i < converters.Count; i++) {
                if (converters[i].CanConvert(targetType))
                    return true;
            }

            return false;
        }

        public static bool TryConvertValue(
            string raw,
            Type targetType,
            Type rowType,
            out object value,
            out string errorMessage) {
            value = null;
            errorMessage = null;
            IReadOnlyList<IMgDataValueConverter> converters = GetValueConverters();
            for (var i = 0; i < converters.Count; i++) {
                IMgDataValueConverter converter = converters[i];
                if (!converter.CanConvert(targetType))
                    continue;

                return converter.TryConvert(
                    raw,
                    targetType,
                    rowType,
                    out value,
                    out errorMessage);
            }

            return false;
        }

        private static IReadOnlyList<IMgDataImportExtension> GetImportExtensions() {
            if (_importExtensions == null)
                _importExtensions = Discover<IMgDataImportExtension>();
            return _importExtensions;
        }

        private static IReadOnlyList<IMgDataSyncOrderProvider> GetSyncOrderProviders() {
            if (_syncOrderProviders == null)
                _syncOrderProviders = Discover<IMgDataSyncOrderProvider>();
            return _syncOrderProviders;
        }

        private static IReadOnlyList<IMgDataValueConverter> GetValueConverters() {
            if (_valueConverters == null)
                _valueConverters = Discover<IMgDataValueConverter>();
            return _valueConverters;
        }

        private static IReadOnlyList<T> Discover<T>() where T : class {
            var result = new List<T>();
            var typeList = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++) {
                Type[] types;
                try {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex) {
                    types = ex.Types;
                }

                for (var j = 0; j < types.Length; j++) {
                    Type type = types[j];
                    if (type == null || type.IsAbstract || type.IsInterface || !typeof(T).IsAssignableFrom(type))
                        continue;
                    typeList.Add(type);
                }
            }

            typeList.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            for (var i = 0; i < typeList.Count; i++) {
                try {
                    if (Activator.CreateInstance(typeList[i]) is T instance)
                        result.Add(instance);
                }
                catch (Exception ex) {
                    UnityEngine.Debug.LogError(
                        $"[MgDataKit] 无法创建扩展 {typeList[i].FullName}：{ex.Message}");
                }
            }

            return result;
        }

        public static bool TrySyncBeforePlay(out string errorMessage) {
            errorMessage = null;
            if (_playModeSyncProviders == null)
                _playModeSyncProviders = Discover<IMgDataPlayModeSyncProvider>();
            for (var i = 0; i < _playModeSyncProviders.Count; i++) {
                if (!_playModeSyncProviders[i].TrySyncBeforePlay(out errorMessage))
                    return false;
            }

            return true;
        }

        public static List<string> BuildRowReferences(
            MgDataBase asset,
            int rowCount,
            string listFieldName) {
            if (_rowReferenceProviders == null)
                _rowReferenceProviders = Discover<IMgDataRowReferenceProvider>();
            for (var i = 0; i < _rowReferenceProviders.Count; i++) {
                if (_rowReferenceProviders[i].CanHandle(asset))
                    return _rowReferenceProviders[i].Build(asset, rowCount, listFieldName);
            }

            var result = new List<string>(Math.Max(0, rowCount));
            for (var i = 0; i < rowCount; i++)
                result.Add($"asset_row={i + 1}");
            return result;
        }
    }
}

#endif
