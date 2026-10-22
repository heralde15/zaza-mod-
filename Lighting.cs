using System;
using Microsoft.Xna.Framework;
using Terraria.Graphics;
using Terraria.Graphics.Light;
using Terraria.ID;
using Terraria.Unified;
using Terraria.Utilities;

namespace Terraria;

// LightMap is never null so long as the area isn't empty.  Unsafe functions do
// not care about this restriction as API consumers are expected to guard
// against out-of-bounds accesses in their own code regardless.
public sealed class LightingEngineExport(LightMap workingMap, Rectangle area)
{
	public Rectangle Area => area;

	public Vector3 GetColor(int x, int y)
	{
		if (!area.Contains(x, y)) {
			return Vector3.Zero;
		}

		x -= area.X;
		y -= area.Y;
		return workingMap[x, y];
	}

	public Vector3 GetColorUnsafe(int x, int y)
	{
		return workingMap[x - area.X, y - area.Y];
	}

	public Vector3 GetColorLocalUnsafe(int localX, int localY)
	{
		return workingMap[localX, localY];
	}
}

public static class Lighting
{
	private const float DEFAULT_GLOBAL_BRIGHTNESS = 1.2f;
	private const float BLIND_GLOBAL_BRIGHTNESS = 1f;
	[Old]
	public static int OffScreenTiles = 45;
	private static LightMode _mode = LightMode.Color;
	private static readonly LightingEngine NewEngine = new LightingEngine();
	private static readonly LegacyLighting LegacyEngine = new LegacyLighting(Main.Camera);
	private static ILightingEngine _activeEngine;

	// This empty export has a null map to reduce memory consumption and extra
	// time spent initializing an export that goes generally unused.  We just
	// need to populate with a sane default value to avoid exceptions before a
	// game world is entered.
	private static readonly LightingEngineExport empty_export = new(workingMap: null, area: Rectangle.Empty);

	public static LightingEngineExport EngineExport { get; private set; } = empty_export;

	public static float GlobalBrightness { get; set; }

	public static LightMode Mode {
		get {
			return _mode;
		}
		set {
			_mode = value;
			switch (_mode) {
				case LightMode.Color:
					_activeEngine = NewEngine;
					LegacyEngine.Mode = 0;
					OffScreenTiles = 35;
					break;
				case LightMode.White:
					_activeEngine = LegacyEngine;
					LegacyEngine.Mode = 1;
					break;
				case LightMode.Retro:
					_activeEngine = LegacyEngine;
					LegacyEngine.Mode = 2;
					break;
				case LightMode.Trippy:
					_activeEngine = LegacyEngine;
					LegacyEngine.Mode = 3;
					break;
			}

			Main.renderCount = 0;
			Main.renderNow = false;
		}
	}

	public static bool NotRetro {
		get {
			if (Mode != LightMode.Retro)
				return Mode != LightMode.Trippy;

			return false;
		}
	}

	public static bool UsingNewLighting => Mode == LightMode.Color;

	public static bool UpdateEveryFrame {
		get {
			if (!Main.RenderTargetsRequired)
				return !NotRetro;

			return false;
		}
	}

	public static void Initialize()
	{
		GlobalBrightness = 1.2f;
		NewEngine.Rebuild();
		LegacyEngine.Rebuild();
		if (_activeEngine == null)
			Mode = LightMode.Color;
	}

	public static void LightTiles(Rectangle area)
	{
		TimeLogger.StartTimestamp fromTimestamp = TimeLogger.Start();
		Main.render = true;
		UpdateGlobalBrightness();
		_activeEngine.ProcessArea(area);
		EngineExport = _activeEngine.GetExport();
		TimeLogger.Lighting.AddTime(fromTimestamp);
	}

	private static void UpdateGlobalBrightness()
	{
		GlobalBrightness = 1.2f;
		if (Main.player[Main.myPlayer].blind)
			GlobalBrightness = 1f;
	}

	public static float Brightness(int x, int y)
	{
		Vector3 color = EngineExport.GetColor(x, y);
		return GlobalBrightness * (color.X + color.Y + color.Z) / 3f;
	}

	public static Vector3 GetSubLight(Vector2 position)
	{
		Vector2 vector = position / 16f - new Vector2(0.5f, 0.5f);
		Vector2 vector2 = new Vector2(vector.X % 1f, vector.Y % 1f);
		int num = (int)vector.X;
		int num2 = (int)vector.Y;
		Vector3 color = EngineExport.GetColor(num, num2);
		Vector3 color2 = EngineExport.GetColor(num + 1, num2);
		Vector3 color3 = EngineExport.GetColor(num, num2 + 1);
		Vector3 color4 = EngineExport.GetColor(num + 1, num2 + 1);
		Vector3 value = Vector3.Lerp(color, color2, vector2.X);
		Vector3 value2 = Vector3.Lerp(color3, color4, vector2.X);
		return Vector3.Lerp(value, value2, vector2.Y);
	}

	public static void AddLight(Vector2 position, Vector3 rgb)
	{
		AddLight((int)(position.X / 16f), (int)(position.Y / 16f), rgb.X, rgb.Y, rgb.Z);
	}

	public static void AddLight(Vector2 position, float r, float g, float b)
	{
		AddLight((int)(position.X / 16f), (int)(position.Y / 16f), r, g, b);
	}

	public static void AddLight(int i, int j, int torchID, float lightAmount)
	{
		TorchID.TorchColor(torchID, out var R, out var G, out var B);
		_activeEngine.AddLight(i, j, new Vector3(R * lightAmount, G * lightAmount, B * lightAmount));
	}

	public static void AddLight(Vector2 position, int torchID)
	{
		TorchID.TorchColor(torchID, out var R, out var G, out var B);
		AddLight((int)position.X / 16, (int)position.Y / 16, R, G, B);
	}

	public static void AddLight(int i, int j, float r, float g, float b)
	{
		if (!Main.gamePaused && Main.netMode != 2)
			_activeEngine.AddLight(i, j, new Vector3(r, g, b));
	}

	public static void NextLightMode()
	{
		Mode++;
		if (!Enum.IsDefined(typeof(LightMode), Mode))
			Mode = LightMode.White;

		Clear();
	}

	public static void Clear()
	{
		_activeEngine.Clear();
	}

	public static Color GetColor(Point tileCoords)
	{
		if (Main.gameMenu)
			return Color.White;

		return new Color(EngineExport.GetColor(tileCoords.X, tileCoords.Y) * GlobalBrightness);
	}

	public static Color GetColor(Point tileCoords, Color originalColor)
	{
		if (Main.gameMenu)
			return originalColor;

		return new Color(EngineExport.GetColor(tileCoords.X, tileCoords.Y) * originalColor.ToVector3());
	}

	public static Color GetColor(int x, int y, Color oldColor)
	{
		if (Main.gameMenu)
			return oldColor;

		return new Color(EngineExport.GetColor(x, y) * oldColor.ToVector3());
	}

	public static Color GetColorClamped(int x, int y, Color oldColor)
	{
		if (Main.gameMenu)
			return oldColor;

		Vector3 color = EngineExport.GetColor(x, y);
		color = Vector3.Min(Vector3.One, color);
		return new Color(color * oldColor.ToVector3());
	}

	public static Color GetColor(int x, int y)
	{
		if (Main.gameMenu)
			return Color.White;

		Color result = default(Color);
		Vector3 color = EngineExport.GetColor(x, y);
		float num = GlobalBrightness * 255f;
		int num2 = (int)(color.X * num);
		int num3 = (int)(color.Y * num);
		int num4 = (int)(color.Z * num);
		if (num2 > 255)
			num2 = 255;

		if (num3 > 255)
			num3 = 255;

		if (num4 > 255)
			num4 = 255;

		num4 <<= 16;
		num3 <<= 8;
		result.PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
		return result;
	}

	public static void GetColor9Slice(int centerX, int centerY, ref Color[] slices)
	{
		/*
		int num = 0;
		for (int i = centerX - 1; i <= centerX + 1; i++) {
			for (int j = centerY - 1; j <= centerY + 1; j++) {
				Vector3 color = EngineExport.GetColor(i, j);
				int num2 = (int)(255f * color.X * GlobalBrightness);
				int num3 = (int)(255f * color.Y * GlobalBrightness);
				int num4 = (int)(255f * color.Z * GlobalBrightness);
				if (num2 > 255)
					num2 = 255;

				if (num3 > 255)
					num3 = 255;

				if (num4 > 255)
					num4 = 255;

				num4 <<= 16;
				num3 <<= 8;
				slices[num].PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
				num += 3;
			}

			num -= 8;
		}
		*/

		var globalBrightness = GlobalBrightness;

		var colors = (Span<Vector3>)stackalloc Vector3[9];
		ColorBuffer.GetSquare(centerX, centerY, colors);

		slices[0] = new Color(colors[0] * globalBrightness);
		slices[1] = new Color(colors[1] * globalBrightness);
		slices[2] = new Color(colors[2] * globalBrightness);
		slices[3] = new Color(colors[3] * globalBrightness);
		slices[4] = new Color(colors[4] * globalBrightness);
		slices[5] = new Color(colors[5] * globalBrightness);
		slices[6] = new Color(colors[6] * globalBrightness);
		slices[7] = new Color(colors[7] * globalBrightness);
		slices[8] = new Color(colors[8] * globalBrightness);
	}

	public static void GetColor9Slice(int x, int y, ref Vector3[] slices)
	{
		/*
		slices[0] = EngineExport.GetColor(x - 1, y - 1) * GlobalBrightness;
		slices[3] = EngineExport.GetColor(x - 1, y) * GlobalBrightness;
		slices[6] = EngineExport.GetColor(x - 1, y + 1) * GlobalBrightness;
		slices[1] = EngineExport.GetColor(x, y - 1) * GlobalBrightness;
		slices[4] = EngineExport.GetColor(x, y) * GlobalBrightness;
		slices[7] = EngineExport.GetColor(x, y + 1) * GlobalBrightness;
		slices[2] = EngineExport.GetColor(x + 1, y - 1) * GlobalBrightness;
		slices[5] = EngineExport.GetColor(x + 1, y) * GlobalBrightness;
		slices[8] = EngineExport.GetColor(x + 1, y + 1) * GlobalBrightness;
		*/

		GetColor9Slice(x, y, slices.AsSpan());
	}

	public static void GetColor9Slice(int x, int y, Span<Vector3> slices)
	{
		var globalBrightness = GlobalBrightness;

		var colors = (Span<Vector3>)stackalloc Vector3[9];
		ColorBuffer.GetSquare(x, y, colors);

		slices[0] = colors[0] * globalBrightness;
		slices[1] = colors[1] * globalBrightness;
		slices[2] = colors[2] * globalBrightness;
		slices[3] = colors[3] * globalBrightness;
		slices[4] = colors[4] * globalBrightness;
		slices[5] = colors[5] * globalBrightness;
		slices[6] = colors[6] * globalBrightness;
		slices[7] = colors[7] * globalBrightness;
		slices[8] = colors[8] * globalBrightness;
	}

	public static void GetCornerColors(int centerX, int centerY, out VertexColors vertices, float scale = 1f)
	{
		vertices = default(VertexColors);
		Vector3 color = EngineExport.GetColor(centerX, centerY);
		Vector3 color2 = EngineExport.GetColor(centerX, centerY - 1);
		Vector3 color3 = EngineExport.GetColor(centerX, centerY + 1);
		Vector3 color4 = EngineExport.GetColor(centerX - 1, centerY);
		Vector3 color5 = EngineExport.GetColor(centerX + 1, centerY);
		Vector3 color6 = EngineExport.GetColor(centerX - 1, centerY - 1);
		Vector3 color7 = EngineExport.GetColor(centerX + 1, centerY - 1);
		Vector3 color8 = EngineExport.GetColor(centerX - 1, centerY + 1);
		Vector3 color9 = EngineExport.GetColor(centerX + 1, centerY + 1);
		float num = GlobalBrightness * scale * 63.75f;
		int num2 = (int)((color2.X + color6.X + color4.X + color.X) * num);
		int num3 = (int)((color2.Y + color6.Y + color4.Y + color.Y) * num);
		int num4 = (int)((color2.Z + color6.Z + color4.Z + color.Z) * num);
		if (num2 > 255)
			num2 = 255;

		if (num3 > 255)
			num3 = 255;

		if (num4 > 255)
			num4 = 255;

		num3 <<= 8;
		num4 <<= 16;
		vertices.TopLeftColor.PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
		num2 = (int)((color2.X + color7.X + color5.X + color.X) * num);
		num3 = (int)((color2.Y + color7.Y + color5.Y + color.Y) * num);
		num4 = (int)((color2.Z + color7.Z + color5.Z + color.Z) * num);
		if (num2 > 255)
			num2 = 255;

		if (num3 > 255)
			num3 = 255;

		if (num4 > 255)
			num4 = 255;

		num3 <<= 8;
		num4 <<= 16;
		vertices.TopRightColor.PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
		num2 = (int)((color3.X + color8.X + color4.X + color.X) * num);
		num3 = (int)((color3.Y + color8.Y + color4.Y + color.Y) * num);
		num4 = (int)((color3.Z + color8.Z + color4.Z + color.Z) * num);
		if (num2 > 255)
			num2 = 255;

		if (num3 > 255)
			num3 = 255;

		if (num4 > 255)
			num4 = 255;

		num3 <<= 8;
		num4 <<= 16;
		vertices.BottomLeftColor.PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
		num2 = (int)((color3.X + color9.X + color5.X + color.X) * num);
		num3 = (int)((color3.Y + color9.Y + color5.Y + color.Y) * num);
		num4 = (int)((color3.Z + color9.Z + color5.Z + color.Z) * num);
		if (num2 > 255)
			num2 = 255;

		if (num3 > 255)
			num3 = 255;

		if (num4 > 255)
			num4 = 255;

		num3 <<= 8;
		num4 <<= 16;
		vertices.BottomRightColor.PackedValue = (uint)(num2 | num3 | num4) | 0xFF000000u;
	}

	public static void GetColor4Slice(int centerX, int centerY, ref Color[] slices)
	{
		/*
		Vector3 color = EngineExport.GetColor(centerX, centerY - 1);
		Vector3 color2 = EngineExport.GetColor(centerX, centerY + 1);
		Vector3 color3 = EngineExport.GetColor(centerX - 1, centerY);
		Vector3 color4 = EngineExport.GetColor(centerX + 1, centerY);
		float num = color.X + color.Y + color.Z;
		float num2 = color2.X + color2.Y + color2.Z;
		float num3 = color4.X + color4.Y + color4.Z;
		float num4 = color3.X + color3.Y + color3.Z;
		if (num >= num4) {
			int num5 = (int)(255f * color3.X * GlobalBrightness);
			int num6 = (int)(255f * color3.Y * GlobalBrightness);
			int num7 = (int)(255f * color3.Z * GlobalBrightness);
			if (num5 > 255)
				num5 = 255;

			if (num6 > 255)
				num6 = 255;

			if (num7 > 255)
				num7 = 255;

			slices[0] = new Color((byte)num5, (byte)num6, (byte)num7, 255);
		}
		else {
			int num8 = (int)(255f * color.X * GlobalBrightness);
			int num9 = (int)(255f * color.Y * GlobalBrightness);
			int num10 = (int)(255f * color.Z * GlobalBrightness);
			if (num8 > 255)
				num8 = 255;

			if (num9 > 255)
				num9 = 255;

			if (num10 > 255)
				num10 = 255;

			slices[0] = new Color((byte)num8, (byte)num9, (byte)num10, 255);
		}

		if (num >= num3) {
			int num11 = (int)(255f * color4.X * GlobalBrightness);
			int num12 = (int)(255f * color4.Y * GlobalBrightness);
			int num13 = (int)(255f * color4.Z * GlobalBrightness);
			if (num11 > 255)
				num11 = 255;

			if (num12 > 255)
				num12 = 255;

			if (num13 > 255)
				num13 = 255;

			slices[1] = new Color((byte)num11, (byte)num12, (byte)num13, 255);
		}
		else {
			int num14 = (int)(255f * color.X * GlobalBrightness);
			int num15 = (int)(255f * color.Y * GlobalBrightness);
			int num16 = (int)(255f * color.Z * GlobalBrightness);
			if (num14 > 255)
				num14 = 255;

			if (num15 > 255)
				num15 = 255;

			if (num16 > 255)
				num16 = 255;

			slices[1] = new Color((byte)num14, (byte)num15, (byte)num16, 255);
		}

		if (num2 >= num4) {
			int num17 = (int)(255f * color3.X * GlobalBrightness);
			int num18 = (int)(255f * color3.Y * GlobalBrightness);
			int num19 = (int)(255f * color3.Z * GlobalBrightness);
			if (num17 > 255)
				num17 = 255;

			if (num18 > 255)
				num18 = 255;

			if (num19 > 255)
				num19 = 255;

			slices[2] = new Color((byte)num17, (byte)num18, (byte)num19, 255);
		}
		else {
			int num20 = (int)(255f * color2.X * GlobalBrightness);
			int num21 = (int)(255f * color2.Y * GlobalBrightness);
			int num22 = (int)(255f * color2.Z * GlobalBrightness);
			if (num20 > 255)
				num20 = 255;

			if (num21 > 255)
				num21 = 255;

			if (num22 > 255)
				num22 = 255;

			slices[2] = new Color((byte)num20, (byte)num21, (byte)num22, 255);
		}

		if (num2 >= num3) {
			int num23 = (int)(255f * color4.X * GlobalBrightness);
			int num24 = (int)(255f * color4.Y * GlobalBrightness);
			int num25 = (int)(255f * color4.Z * GlobalBrightness);
			if (num23 > 255)
				num23 = 255;

			if (num24 > 255)
				num24 = 255;

			if (num25 > 255)
				num25 = 255;

			slices[3] = new Color((byte)num23, (byte)num24, (byte)num25, 255);
		}
		else {
			int num26 = (int)(255f * color2.X * GlobalBrightness);
			int num27 = (int)(255f * color2.Y * GlobalBrightness);
			int num28 = (int)(255f * color2.Z * GlobalBrightness);
			if (num26 > 255)
				num26 = 255;

			if (num27 > 255)
				num27 = 255;

			if (num28 > 255)
				num28 = 255;

			slices[3] = new Color((byte)num26, (byte)num27, (byte)num28, 255);
		}
		*/

		var globalBrightness = GlobalBrightness;

		var colors = (Span<Vector3>)stackalloc Vector3[4];
		ColorBuffer.GetPlus(centerX, centerY, colors);

		var color = colors[0];
		var color2 = colors[1];
		var color3 = colors[2];
		var color4 = colors[3];

		var total1 = color.X + color.Y + color.Z;
		var total2 = color2.X + color2.Y + color2.Z;
		var total3 = color4.X + color4.Y + color4.Z;
		var total4 = color3.X + color3.Y + color3.Z;

		slices[0] = new Color((total1 >= total4 ? color3 : color) * globalBrightness);
		slices[1] = new Color((total1 >= total3 ? color4 : color) * globalBrightness);
		slices[2] = new Color((total2 >= total4 ? color3 : color2) * globalBrightness);
		slices[3] = new Color((total2 >= total3 ? color4 : color2) * globalBrightness);
	}

	public static void GetColor4Slice(int x, int y, ref Vector3[] slices)
	{
		/*
		Vector3 color = EngineExport.GetColor(x, y - 1);
		Vector3 color2 = EngineExport.GetColor(x, y + 1);
		Vector3 color3 = EngineExport.GetColor(x - 1, y);
		Vector3 color4 = EngineExport.GetColor(x + 1, y);
		float num = color.X + color.Y + color.Z;
		float num2 = color2.X + color2.Y + color2.Z;
		float num3 = color4.X + color4.Y + color4.Z;
		float num4 = color3.X + color3.Y + color3.Z;
		if (num >= num4)
			slices[0] = color3 * GlobalBrightness;
		else
			slices[0] = color * GlobalBrightness;

		if (num >= num3)
			slices[1] = color4 * GlobalBrightness;
		else
			slices[1] = color * GlobalBrightness;

		if (num2 >= num4)
			slices[2] = color3 * GlobalBrightness;
		else
			slices[2] = color2 * GlobalBrightness;

		if (num2 >= num3)
			slices[3] = color4 * GlobalBrightness;
		else
			slices[3] = color2 * GlobalBrightness;
		*/

		GetColor4Slice(x, y, slices.AsSpan());
	}

	public static void GetColor4Slice(int x, int y, Span<Vector3> slices)
	{
		var globalBrightness = GlobalBrightness;

		var colors = (Span<Vector3>)stackalloc Vector3[4];
		ColorBuffer.GetPlus(x, y, colors);

		var color = colors[0];
		var color2 = colors[1];
		var color3 = colors[2];
		var color4 = colors[3];

		var total1 = color.X + color.Y + color.Z;
		var total2 = color2.X + color2.Y + color2.Z;
		var total3 = color4.X + color4.Y + color4.Z;
		var total4 = color3.X + color3.Y + color3.Z;

		slices[0] = (total1 >= total4 ? color3 : color) * globalBrightness;
		slices[1] = (total1 >= total3 ? color4 : color) * globalBrightness;
		slices[2] = (total2 >= total4 ? color3 : color2) * globalBrightness;
		slices[3] = (total2 >= total3 ? color4 : color2) * globalBrightness;
	}

	internal static void AddGameplaySnapshotComponents()
	{
		NewEngine.AddGameplaySnapshotComponents();
	}
}
