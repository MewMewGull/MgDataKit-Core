#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEngine;

namespace MgDataKit.Editor {
    /// <summary>
    /// Parses source-neutral scalar values after an external source adapter has produced a grid.
    /// Source readers and asset-reference resolution belong to integration packages.
    /// </summary>
    internal static class MgDataValueParser {
        private static readonly string[] KnownTypeNames = {
            "int",
            "long",
            "float",
            "double",
            "bool",
            "string",
            "Vector2",
            "Vector3",
            "Vector2Int",
            "Vector3Int",
            "Color",
            "ColorHex",
            "enum",
            "List<int>",
            "List<float>",
            "List<string>"
        };

        public static bool IsKnownTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            string trimmed = typeName.Trim();
            for (var i = 0; i < KnownTypeNames.Length; i++) {
                if (string.Equals(KnownTypeNames[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static Vector2 ParseVector2(string input) {
            string[] parts = SplitVector(input, 2, "Vector2");
            return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
        }

        public static Vector2Int ParseVector2Int(string input) {
            string[] parts = SplitVector(input, 2, "Vector2Int");
            return new Vector2Int(ParseInt(parts[0]), ParseInt(parts[1]));
        }

        public static Vector3 ParseVector3(string input) {
            string[] parts = SplitVector(input, 3, "Vector3");
            return new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]));
        }

        public static Vector3Int ParseVector3Int(string input) {
            string[] parts = SplitVector(input, 3, "Vector3Int");
            return new Vector3Int(ParseInt(parts[0]), ParseInt(parts[1]), ParseInt(parts[2]));
        }

        public static Color ParseColor(string input) {
            if (string.IsNullOrWhiteSpace(input))
                return default;
            if (!ColorHex.TryParse(input, out ColorHex color))
                throw new FormatException($"ColorHex 格式错误: {input}");
            return color;
        }

        private static string[] SplitVector(string input, int expectedCount, string typeName) {
            string normalized = input?.Trim(' ', '(', ')') ?? string.Empty;
            string[] parts = normalized.Split(',');
            if (parts.Length != expectedCount)
                throw new FormatException($"{typeName} 格式错误");
            return parts;
        }

        private static int ParseInt(string value) {
            return int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static float ParseFloat(string value) {
            return float.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
#endif
