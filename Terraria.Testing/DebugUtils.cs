using System.Drawing;
using System.Numerics;
using Terraria.ID;
using Terraria.Testing.ChatCommands;
using Terraria.UI.Chat;

namespace Terraria.Testing;

public static class DebugUtils
{
	private static char[] _slopeIcons = new char[5] { '⬓', '◣', '◢', '◤', '◥' };

	internal static string GetTileDescription(int x, int y)
	{
		//IL_0358: Unknown result type (might be due to invalid IL or missing references)
		//IL_035d: Unknown result type (might be due to invalid IL or missing references)
		Tile tile = Main.tile[x, y];
		Point point = (Main.LocalPlayer.Bottom + new Vector2(-8f, 8f)).ToTileCoordinates();
		string name = default(string);
		if (!TileID.Search.TryGetName((int)tile.type, ref name))
		{
			name = "Unknown";
		}
		string name2 = default(string);
		if (!WallID.Search.TryGetName((int)tile.wall, ref name2))
		{
			name2 = "Unknown";
		}
		string text = "   ";
		return text + "Pos: " + x + ", " + y + "\n" + text + "Type: " + tile.type + ((tile.blockType() == 0) ? "" : (" " + _slopeIcons[tile.blockType() - 1])) + " (" + name + ")\n" + text + "Frame: " + tile.frameX + ", " + tile.frameY + "\n" + text + "FrameImportant: " + Main.tileFrameImportant[tile.type] + "\n" + text + "Liquid: " + tile.liquid + " (" + tile.liquidType() + ")\n" + text + "Wall: " + tile.wall + " (" + name2 + ")\n" + text + "Compare Spot: " + point.X + ", " + point.Y + "\n" + text + "Chunk: " + x / 200 + ", " + y / 150 + "\n" + text + "Paints: " + tile.color() + (tile.fullbrightBlock() ? " fullbright" : "") + (tile.invisibleBlock() ? " echo" : "") + ", " + tile.wallColor() + (tile.fullbrightWall() ? " fullbright" : "") + (tile.invisibleWall() ? " echo" : "") + "\n" + text + "Light: " + ((object)Lighting.GetColor(x, y)/*cast due to constrained. prefix*/).ToString();
	}

	public static void QuickSPMessage(string message)
	{
		ChatManager.DebugCommands.Process(new DebugMessage((byte)Main.myPlayer, message));
	}
}
