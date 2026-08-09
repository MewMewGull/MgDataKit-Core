using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 支持用十六进制 uint (RRGGBBAA，高位→低位依次为 R、G、B、A) 构造，可隐式转换为 Unity Color。
/// 用法：<c>Color c = new ColorHex(0xFF2D21FF);</c>
/// </summary>
public readonly struct ColorHex {
    public readonly uint Rgba;

    public ColorHex(uint rgba) {
        Rgba = rgba;
    }

    public static implicit operator Color(ColorHex hex) {
        var r = (byte)(hex.Rgba >> 24);
        var g = (byte)(hex.Rgba >> 16);
        var b = (byte)(hex.Rgba >> 8);
        var a = (byte)hex.Rgba;
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    public static implicit operator ColorHex(Color c) {
        return FromColor(c);
    }

    public static ColorHex FromColor(Color c) {
        var r = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
        var g = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
        var b = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        var a = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f);
        var rgba = ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        return new ColorHex(rgba);
    }

    /// <summary>
    /// 解析颜色文本：<c>#RRGGBB</c>、<c>#RRGGBBAA</c>、可选 <c>0x</c> 前缀；6 位时 A 视为 FF。
    /// </summary>
    public static bool TryParse(string input, out ColorHex hex) {
        hex = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var s = input.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal))
            s = s.Substring(1);
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(2);
        if (s.Length == 6) {
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return false;
            hex = new ColorHex((rgb << 8) | 0xFF);
            return true;
        }
        if (s.Length != 8)
            return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return false;
        hex = new ColorHex(rgba);
        return true;
    }

    /// <summary>固定 8 位十六进制，带 <c>#</c> 前缀。</summary>
    public string ToHexString() {
        return "#" + Rgba.ToString("X8");
    }

    /// <summary>将 <see cref="Color"/> 转为 MgDataKit 使用的十六进制字符串。</summary>
    public static string ToHexString(Color c) {
        return FromColor(c).ToHexString();
    }
}
