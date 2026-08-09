using UnityEngine;

namespace MgDataKit {
    public enum EMgDataKitSettingsLocation {
        [InspectorName("Editor Directory")]
        EditorDirectory,

        [InspectorName("Assets Resources")]
        AssetsResources,

        [InspectorName("MgDataKit Directory")]
        MgDataKitDirectory
    }

    /// <summary>
    /// MgDataKit 项目级配置。项目中必须有且仅有一个实例。
    /// </summary>
    public sealed class MgDataKitSettings : ScriptableObject {
        [SerializeField]
        private EMgDataKitSettingsLocation _storageLocation = EMgDataKitSettingsLocation.EditorDirectory;

        [Header("Project Defaults")]
        [SerializeField]
        private bool _autoImportEnabled = true;

        [SerializeField]
        private bool _automaticLintEnabled = true;

        public EMgDataKitSettingsLocation StorageLocation {
            get => _storageLocation;
            set => _storageLocation = value;
        }

        public bool AutoImportEnabled {
            get => _autoImportEnabled;
            set => _autoImportEnabled = value;
        }

        public bool AutomaticLintEnabled {
            get => _automaticLintEnabled;
            set => _automaticLintEnabled = value;
        }

    }
}
