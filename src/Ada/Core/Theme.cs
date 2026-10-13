using Vortice.Mathematics;

namespace Ada.Core;

public static class Theme
{
    public const string Font = "Segoe UI Variable Display";
    public const string TextFont = "Segoe UI Variable Text";
    public const string IconFont = "Segoe Fluent Icons";

    public static Color4 Rgb(uint hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    public static readonly Color4 Island = Rgb(0x0E1119);
    public static readonly Color4 Orange = Rgb(0xF5A97F);
    public static readonly Color4 TileOn = Rgb(0xA6DDA0);
    public static readonly Color4 TileOnText = Rgb(0x14241A);
    public static readonly Color4 Surface = Rgb(0xFFFFFF, 0.055f);
    public static readonly Color4 SurfaceHi = Rgb(0xFFFFFF, 0.10f);
    public static readonly Color4 Stroke = Rgb(0xFFFFFF, 0.08f);
    public static readonly Color4 Text = Rgb(0xF5F5F7);
    public static readonly Color4 Text2 = Rgb(0xF5F5F7, 0.56f);
    public static readonly Color4 Text3 = Rgb(0xF5F5F7, 0.32f);

    public static Color4 Accent = Rgb(0xA6E3A1);
    public static readonly Color4 Mint = Rgb(0x5EEAD4);
    public static readonly Color4 Sky = Rgb(0x7DB4FF);
    public static readonly Color4 Pink = Rgb(0xF59AC9);
    public static readonly Color4 Amber = Rgb(0xFBBF5B);
    public static readonly Color4 Red = Rgb(0xFF6B6B);
    public static readonly Color4 Green = Rgb(0x4ADE80);

    public static Color4 WithA(this Color4 c, float a) => new(c.R, c.G, c.B, c.A * a);
    public static Color4 Mix(Color4 a, Color4 b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
}

/// <summary>Segoe Fluent Icons glifleri.</summary>
public static class Glyph
{
    public const string Play = "", Pause = "", Prev = "", Next = "", Music = "";
    public const string Volume = "", Mute = "", Brightness = "", Wifi = "", Bluetooth = "";
    public const string Moon = "", Lock = "", Restart = "", Power = "", Sleep = "";
    public const string Pulse = "", Sliders = "", Clipboard = "", Tasks = "", Apps = "";
    public const string Add = "", Delete = "", Check = "", Bell = "", Copy = "";
    public const string Clear = "", Left = "", Right = "", Battery = "", Charging = "";
    public const string Down = "", Up = "", Clock = "", Disk = "", Calendar = "";
    public const string Settings = "", Folder = "", Theme = "", Sun = "", Contrast = "", Notch = "";
}
