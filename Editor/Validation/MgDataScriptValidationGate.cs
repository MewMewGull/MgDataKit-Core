using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MgDataKit.Editor {
    [InitializeOnLoad]
    public static class MgDataScriptValidationGate {
        private static readonly List<string> Errors = new();
        private static readonly List<string> PersistedLintLogs = new();
        private static readonly List<string> PersistedLintWarnings = new();
        private const string GateErrorLog = "<color=red>[MgDataKit]</color> 检测到脚本字段校验错误，已阻止进入 Play 模式。请修复导表错误后重试。";
        private const string ScriptLintErrorPrefix = "<color=red>[MgDataKit][ScriptLint][Error]</color> ";
        private const string ScriptLintWarningPrefix = "<color=yellow>[MgDataKit][ScriptLint][Warning]</color> ";
        private static readonly MethodInfo GetCountMethod = ResolveGetCountMethod();
        private static bool _hasError;

        static MgDataScriptValidationGate() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += () => ValidateAllImportedTables(false);
            EditorApplication.update += OnEditorUpdate;
        }

        public static bool GetDisableAutomaticLint() {
            return !MgDataKitUserPreferencesStore.GetAutomaticLintEnabled();
        }

        public static bool ValidateAllImportedTables(bool force = false, bool includeSourceRowReferences = true) {
            if (!force && GetDisableAutomaticLint())
                return true;

            if (!MgDataKitAssetCatalogProvider.TryEnsureCatalogReady(out _, out var catalogError)) {
                Errors.Clear();
                PersistedLintLogs.Clear();
                PersistedLintWarnings.Clear();
                _hasError = false;
                if (force)
                    Debug.LogWarning($"[MgDataKit] Asset Catalog 未就绪，跳过 Lint：{catalogError}");
                return true;
            }
            Errors.Clear();
            PersistedLintLogs.Clear();
            PersistedLintWarnings.Clear();
            MgDataAttributeLintValidator.Validate(Errors, includeSourceRowReferences);

            var hasAnyIssue = Errors.Count > 0;
            _hasError = false;
            if (hasAnyIssue) {
                for (var i = 0; i < Errors.Count; i++) {
                    EMgDataLintSeverity severity = ParseSeverity(Errors[ i ]);
                    var lintLog = FormatScriptLintLog(Errors[ i ], severity);
                    if (severity == EMgDataLintSeverity.Warning) {
                        PersistedLintWarnings.Add(lintLog);
                        Debug.LogWarning(lintLog);
                    }
                    else {
                        _hasError = true;
                        PersistedLintLogs.Add(lintLog);
                        Debug.LogError(lintLog);
                    }
                }

                if (!_hasError)
                    Debug.Log("<color=yellow>[MgDataKit]</color> 存在 ScriptLint Warning，不阻止进入 Play 模式。");
            }
            else {
                Debug.Log("<color=green>[MgDataKit]</color> 脚本字段校验通过");
            }

            return !_hasError;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            if (!MgDataKitExtensionRegistry.TrySyncBeforePlay(out var syncError)) {
                EditorApplication.isPlaying = false;
                Debug.LogError(
                    $"<color=red>[MgDataKit]</color> 数据源同步失败，已阻止进入 Play 模式。\n{syncError}");
                return;
            }

            if (_hasError && !GetDisableAutomaticLint()) {
                EditorApplication.isPlaying = false;
                EditorUtility.DisplayDialog(
                    "MgDataKit 校验失败",
                    "检测到脚本字段校验错误，已阻止进入 Play 模式。\n请修复导表错误后重试。",
                    "知道了");
                Debug.LogError(GateErrorLog);
            }
        }

        private static void OnEditorUpdate() {
            // 模拟编译错误体验：如果有导表错误且 Console 被清空，则自动补回关键阻止提示。
            if (_hasError && !GetDisableAutomaticLint() && IsConsoleEmpty())
                ReplayPersistentErrors();
        }

        private static void ReplayPersistentErrors() {
            for (var i = 0; i < PersistedLintLogs.Count; i++)
                Debug.LogError(PersistedLintLogs[ i ]);
            for (var i = 0; i < PersistedLintWarnings.Count; i++)
                Debug.LogWarning(PersistedLintWarnings[ i ]);
            Debug.LogError(GateErrorLog);
        }

        private static string FormatScriptLintLog(string rawError, EMgDataLintSeverity severity) {
            var prefix = severity == EMgDataLintSeverity.Warning ? ScriptLintWarningPrefix : ScriptLintErrorPrefix;
            if (string.IsNullOrEmpty(rawError))
                return prefix;

            var normalized = StripScriptLintPrefix(rawError).Trim();
            ExtractSummaryAndDetails(normalized, out var summary, out var details);
            if (details.Count <= 0)
                return $"{prefix}{summary}";

            var formatted = $"{prefix}{summary}\n  details:";
            for (var i = 0; i < details.Count; i++) {
                var pair = details[ i ];
                if (string.Equals(pair.Key, "rows", StringComparison.OrdinalIgnoreCase)) {
                    formatted += "\n    rows:";
                    formatted += FormatDuplicateRowsYamlBlock(pair.Value);
                    continue;
                }

                formatted += $"\n    {pair.Key}: {pair.Value}";
            }

            return formatted;
        }

        private static string FormatDuplicateRowsYamlBlock(string rawRows) {
            if (string.IsNullOrWhiteSpace(rawRows))
                return "\n      - <empty>";

            var text = string.Empty;
            var groups = rawRows.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < groups.Length; i++) {
                var group = groups[ i ].Trim();
                if (string.IsNullOrEmpty(group))
                    continue;

                var atIndex = group.IndexOf('@');
                if (atIndex <= 0 || atIndex >= group.Length - 1) {
                    text += $"\n      - {group}";
                    continue;
                }

                var value = group.Substring(0, atIndex).Trim();
                var rowsPart = group.Substring(atIndex + 1).Trim();
                text += $"\n      - value: {value}";
                text += "\n        positions:";

                var rowTokens = rowsPart.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (rowTokens.Length == 0) {
                    text += "\n          - <unknown>";
                    continue;
                }

                for (var r = 0; r < rowTokens.Length; r++) {
                    var token = rowTokens[ r ].Trim();
                    if (string.IsNullOrEmpty(token))
                        continue;
                    text += $"\n          - {token}";
                }
            }

            return string.IsNullOrEmpty(text) ? "\n      - <empty>" : text;
        }

        private static void ExtractSummaryAndDetails(string normalized, out string summary,
            out List<KeyValuePair<string, string>> details) {
            details = new List<KeyValuePair<string, string>>();
            summary = normalized ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            var keyStart = FindFirstKeyStartIndex(normalized);
            if (keyStart < 0) {
                summary = normalized.Trim();
                return;
            }

            summary = normalized.Substring(0, keyStart).Trim();
            var kvText = normalized.Substring(keyStart).Trim();
            var segments = kvText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++) {
                var segment = segments[ i ].Trim();
                if (string.IsNullOrEmpty(segment))
                    continue;

                var sepIndex = segment.IndexOf('=');
                if (sepIndex <= 0 || sepIndex >= segment.Length - 1)
                    continue;

                var key = segment.Substring(0, sepIndex).Trim();
                var value = segment.Substring(sepIndex + 1).Trim();
                if (string.IsNullOrEmpty(key))
                    continue;
                details.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        private static int FindFirstKeyStartIndex(string text) {
            if (string.IsNullOrEmpty(text))
                return -1;

            for (var i = 0; i < text.Length; i++) {
                var c = text[ i ];
                if (!(char.IsLetter(c) || c == '_'))
                    continue;

                var eqIndex = text.IndexOf('=', i);
                if (eqIndex < 0)
                    return -1;

                var token = text.Substring(i, eqIndex - i).Trim();
                if (IsKeyToken(token))
                    return i;
            }

            return -1;
        }

        private static bool IsKeyToken(string token) {
            if (string.IsNullOrEmpty(token))
                return false;

            for (var i = 0; i < token.Length; i++) {
                var c = token[ i ];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }

            return true;
        }

        private static string StripScriptLintPrefix(string text) {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = text.Trim();
            result = result.Replace("[MgDataKit][ScriptLint][Error]", string.Empty);
            result = result.Replace("[MgDataKit][ScriptLint][Warning]", string.Empty);
            result = result.Replace("<color=red>[MgDataKit][ScriptLint]</color>", string.Empty);
            result = result.Replace("[MgDataKit][ScriptLint]", string.Empty);
            return result.Trim();
        }

        private static EMgDataLintSeverity ParseSeverity(string lintText) {
            if (string.IsNullOrEmpty(lintText))
                return EMgDataLintSeverity.Error;
            if (lintText.Contains("[MgDataKit][ScriptLint][Warning]", StringComparison.Ordinal))
                return EMgDataLintSeverity.Warning;
            return EMgDataLintSeverity.Error;
        }

        private static bool IsConsoleEmpty() {
            if (GetCountMethod == null)
                return false;

            var result = GetCountMethod.Invoke(null, null);
            return result is int count && count == 0;
        }

        private static MethodInfo ResolveGetCountMethod() {
            Type logEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
            if (logEntriesType == null)
                return null;

            return logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
