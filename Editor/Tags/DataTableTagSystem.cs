#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    public sealed class DataTableTagDefinition {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string _name;

        public string Id => _id ?? string.Empty;
        public string Name => _name ?? string.Empty;

        internal DataTableTagDefinition(string name) {
            _id = Guid.NewGuid().ToString("N");
            _name = DataTableTagSystem.NormalizeName(name);
        }

        private DataTableTagDefinition() {
        }

        internal bool EnsureId(ISet<string> usedIds) {
            if (!string.IsNullOrWhiteSpace(_id) && usedIds.Add(_id))
                return false;

            do {
                _id = Guid.NewGuid().ToString("N");
            } while (!usedIds.Add(_id));
            return true;
        }

        internal void SetName(string name) {
            _name = DataTableTagSystem.NormalizeName(name);
        }
    }

    [Serializable]
    public sealed class DataTableTagSystem {
        [SerializeField]
        private List<DataTableTagDefinition> _definitions = new();

        public IReadOnlyList<DataTableTagDefinition> Definitions => MutableDefinitions;

        internal List<DataTableTagDefinition> MutableDefinitions =>
            _definitions ??= new List<DataTableTagDefinition>();

        public DataTableTagDefinition FindById(string tagId) {
            if (string.IsNullOrWhiteSpace(tagId))
                return null;

            List<DataTableTagDefinition> definitions = MutableDefinitions;
            for (var i = 0; i < definitions.Count; i++) {
                DataTableTagDefinition definition = definitions[i];
                if (definition != null && string.Equals(definition.Id, tagId, StringComparison.Ordinal))
                    return definition;
            }

            return null;
        }

        public DataTableTagDefinition FindByName(string name) {
            string normalizedName = NormalizeName(name);
            if (normalizedName.Length == 0)
                return null;

            List<DataTableTagDefinition> definitions = MutableDefinitions;
            for (var i = 0; i < definitions.Count; i++) {
                DataTableTagDefinition definition = definitions[i];
                if (definition != null && string.Equals(
                        definition.Name,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase))
                    return definition;
            }

            return null;
        }

        internal DataTableTagDefinition Add(string name, out string errorMessage) {
            string normalizedName = NormalizeName(name);
            if (normalizedName.Length == 0) {
                errorMessage = "Tag 名称不能为空。";
                return null;
            }

            if (FindByName(normalizedName) != null) {
                errorMessage = $"Tag 名称已存在：{normalizedName}";
                return null;
            }

            var definition = new DataTableTagDefinition(normalizedName);
            MutableDefinitions.Add(definition);
            errorMessage = null;
            return definition;
        }


        internal bool TryRename(
            DataTableTagDefinition definition,
            string name,
            out string errorMessage) {
            if (definition == null || !MutableDefinitions.Contains(definition)) {
                errorMessage = "Tag 不属于当前目录。";
                return false;
            }

            string normalizedName = NormalizeName(name);
            if (normalizedName.Length == 0) {
                errorMessage = "Tag 名称不能为空。";
                return false;
            }

            DataTableTagDefinition duplicate = FindByName(normalizedName);
            if (duplicate != null && duplicate != definition) {
                errorMessage = $"Tag 名称已存在：{normalizedName}";
                return false;
            }

            definition.SetName(normalizedName);
            errorMessage = null;
            return true;
        }

        internal bool Remove(DataTableTagDefinition definition) {
            return definition != null && MutableDefinitions.Remove(definition);
        }

        internal bool Normalize() {
            var changed = false;
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<DataTableTagDefinition> definitions = MutableDefinitions;
            for (var i = 0; i < definitions.Count;) {
                DataTableTagDefinition definition = definitions[i];
                string normalizedName = definition != null ? NormalizeName(definition.Name) : string.Empty;
                if (definition == null || normalizedName.Length == 0 || !usedNames.Add(normalizedName)) {
                    definitions.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!string.Equals(definition.Name, normalizedName, StringComparison.Ordinal)) {
                    definition.SetName(normalizedName);
                    changed = true;
                }

                changed |= definition.EnsureId(usedIds);
                i++;
            }

            return changed;
        }

        internal static string NormalizeName(string name) {
            return (name ?? string.Empty).Trim();
        }
    }

    [Serializable]
    public sealed class DataTableTagContainer {
        [SerializeField]
        private List<string> _tagIds = new();

        public IReadOnlyList<string> TagIds => MutableTagIds;

        internal List<string> MutableTagIds => _tagIds ??= new List<string>();

        public bool Contains(string tagId) {
            if (string.IsNullOrWhiteSpace(tagId))
                return false;

            List<string> tagIds = MutableTagIds;
            for (var i = 0; i < tagIds.Count; i++) {
                if (string.Equals(tagIds[i], tagId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        internal bool Add(string tagId) {
            if (string.IsNullOrWhiteSpace(tagId) || Contains(tagId))
                return false;
            MutableTagIds.Add(tagId);
            return true;
        }

        internal bool Remove(string tagId) {
            var changed = false;
            List<string> tagIds = MutableTagIds;
            for (var i = tagIds.Count - 1; i >= 0; i--) {
                if (!string.Equals(tagIds[i], tagId, StringComparison.Ordinal))
                    continue;
                tagIds.RemoveAt(i);
                changed = true;
            }

            return changed;
        }

        internal bool Prune(DataTableTagSystem tagSystem) {
            var changed = false;
            var knownIds = new HashSet<string>(StringComparer.Ordinal);
            if (tagSystem != null) {
                IReadOnlyList<DataTableTagDefinition> definitions = tagSystem.Definitions;
                for (var i = 0; i < definitions.Count; i++) {
                    DataTableTagDefinition definition = definitions[i];
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.Id))
                        knownIds.Add(definition.Id);
                }
            }

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            List<string> tagIds = MutableTagIds;
            for (var i = 0; i < tagIds.Count;) {
                string tagId = tagIds[i];
                if (!knownIds.Contains(tagId) || !usedIds.Add(tagId)) {
                    tagIds.RemoveAt(i);
                    changed = true;
                    continue;
                }

                i++;
            }

            return changed;
        }
    }
}
#endif
