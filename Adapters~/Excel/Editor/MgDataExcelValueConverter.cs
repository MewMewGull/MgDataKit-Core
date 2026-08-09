#if UNITY_EDITOR

using System;
using MgDataKit;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MgDataKit.Editor {
    /// <summary>
    /// Adds Addressables-backed Unity object fields without making Core depend on Addressables.
    /// </summary>
    internal sealed class MgDataExcelValueConverter : IMgDataValueConverter {
        public bool CanConvert(Type targetType) {
            if (targetType == null || !typeof(Object).IsAssignableFrom(targetType))
                return false;
            if (targetType == typeof(Object) || typeof(Component).IsAssignableFrom(targetType))
                return false;
            return !typeof(MgDataBase).IsAssignableFrom(targetType);
        }

        public bool IsKnownTypeName(string typeName) {
            return MgDataUtils.IsKnownExcelTypeName(typeName);
        }

        public bool TryConvert(
            string raw,
            Type targetType,
            Type rowType,
            out object value,
            out string errorMessage) {
            errorMessage = null;
            value = MgDataUtils.LoadAddressableAssetForImport(raw, targetType, rowType);
            return true;
        }
    }
}

#endif
