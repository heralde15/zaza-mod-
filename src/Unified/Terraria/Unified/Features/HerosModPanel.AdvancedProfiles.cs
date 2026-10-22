using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace Terraria.Unified.Features;

/// <summary>
/// Modulo complementario de HERO's Mod: perfiles de estadísticas temporales y
/// marcadores de teletransporte. Los perfiles viven en memoria y se pierden al
/// cerrar el juego. En multijugador el servidor puede corregir estadísticas.
/// </summary>
internal static partial class HerosModPanel
{
    private sealed class PlayerProfileSnapshot
    {
        public bool Valid;
        public string Name = "";
        public int Life;
        public int MaxLife;
        public int Mana;
        public int MaxMana;
        public int SelectedSlot;
        public int Defense;
        public int TileX;
        public int TileY;
        public float MoveSpeed;
        public float RunAcceleration;
        public float MaxRunSpeed;
        public bool NoKnockback;
        public bool FireWalk;
        public bool LavaImmune;
        public bool FallImmune;
    }

    private readonly record struct TeleportBookmark(bool Valid, string Name, int X, int Y);

    private static readonly PlayerProfileSnapshot[] profileSlots =
    {
        new(), new(), new(), new()
    };

    private static TeleportBookmark[] teleportBookmarks =
    {
        new(false, "", 0, 0),
        new(false, "", 0, 0),
        new(false, "", 0, 0),
        new(false, "", 0, 0),
        new(false, "", 0, 0),
        new(false, "", 0, 0)
    };

    private static string profileNameInput = "Mi perfil";
    private static string bookmarkNameInput = "Marcador";
    private static string bookmarkXInput = "";
    private static string bookmarkYInput = "";
    private static int profilePage;
    private static int bookmarkPage;
    private static int selectedProfile = -1;
    private static int selectedBookmark = -1;
    private static bool profileConfirmRestore;
    private static int profileNoticeTimer;

    private static void DrawProfilesTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        Player player = Main.LocalPlayer;
        DrawSectionHeader(sb, panel, ref y, "Perfiles y accesos rápidos");
        Utils.DrawBorderString(sb, "Guarda estadísticas y efectos seleccionados en 4 espacios temporales.",
            new Vector2(panel.X + Pad, y), new Color(190, 205, 225), 0.68f);
        y += 24;

        Utils.DrawBorderString(sb, "Nombre del perfil:", new Vector2(panel.X + Pad, y), Color.White, 0.68f);
        y += 17;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 270, 24), 31, profileNameInput, mouse, "Nombre del perfil");
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 280, y, 150, 24), "Guardar actual", mouse, new Color(45, 105, 75)))
            SavePlayerProfile(profilePage);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 438, y, 170, 24), "Restaurar perfil", mouse, new Color(55, 85, 130)))
        {
            if (profilePage >= 0 && profilePage < profileSlots.Length && profileSlots[profilePage].Valid)
            {
                RestorePlayerProfile(profilePage);
            }
            else
            {
                SetStatus("Primero guarda un perfil en ese espacio.");
            }
        }
        y += 32;

        for (int i = 0; i < profileSlots.Length; i++)
        {
            PlayerProfileSnapshot p = profileSlots[i];
            int rowY = y + i * 31;
            Color c = i == profilePage ? new Color(56, 80, 105) : new Color(38, 42, 52);
            sb.Draw(pixel, new Rectangle(panel.X + Pad, rowY, Width - Pad * 2, 27), c);
            string summary = p.Valid
                ? $"{i + 1}. {p.Name}  | Vida {p.Life}/{p.MaxLife}  | Maná {p.Mana}/{p.MaxMana}  | Defensa {p.Defense}"
                : $"{i + 1}. (vacío)";
            Utils.DrawBorderString(sb, summary, new Vector2(panel.X + Pad + 7, rowY + 5), Color.White, 0.68f);
            if (new Rectangle(panel.X + Pad, rowY, Width - Pad * 2, 27).Contains(mouse) && Clicked)
            {
                Main.mouseLeftRelease = false;
                profilePage = i;
                selectedProfile = i;
                if (p.Valid)
                    profileNameInput = p.Name;
            }
        }
        y += profileSlots.Length * 31 + 8;

        DrawSectionHeader(sb, panel, ref y, "Acciones rápidas");
        int bw = (Width - Pad * 2 - 12) / 3;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, 25), "Curar al máximo", mouse, new Color(45, 105, 65)))
        {
            player.statLife = player.statLifeMax2;
            player.statMana = player.statManaMax2;
            SyncSelf();
            SetStatus("Vida y maná restaurados localmente.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 6, y, bw, 25), "Vida al 50%", mouse))
        {
            player.statLife = Math.Max(1, player.statLifeMax2 / 2);
            SyncSelf();
            SetStatus("Vida ajustada al 50%.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + (bw + 6) * 2, y, bw, 25), "Maná al 50%", mouse))
        {
            player.statMana = Math.Max(0, player.statManaMax2 / 2);
            SyncSelf();
            SetStatus("Maná ajustado al 50%.");
        }
        y += 31;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, 25), "Quitar todos los buffs", mouse, new Color(110, 65, 55)))
        {
            for (int i = 0; i < player.buffType.Length; i++)
            {
                if (player.buffType[i] > 0)
                    player.DelBuff(i);
            }
            SyncSelf();
            SetStatus("Se quitaron los buffs activos del jugador local.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 6, y, bw, 25), "Volver a posición guardada", mouse))
        {
            if (profilePage >= 0 && profilePage < profileSlots.Length && profileSlots[profilePage].Valid)
            {
                PlayerProfileSnapshot p = profileSlots[profilePage];
                teleportX = p.TileX.ToString();
                teleportY = p.TileY.ToString();
                TeleportToCoordinates();
            }
            else
                SetStatus("Guarda primero un perfil.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + (bw + 6) * 2, y, bw, 25), "Limpiar selección", mouse))
        {
            selectedProfile = -1;
            profileConfirmRestore = false;
            SetStatus("Selección del perfil limpiada.");
        }
        y += 34;

        DrawSectionHeader(sb, panel, ref y, "Marcadores de teletransporte");
        Utils.DrawBorderString(sb, "X:", new Vector2(panel.X + Pad, y + 4), Color.White, 0.7f);
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + 20, y, 80, 24), 32, bookmarkXInput, mouse, "X");
        Utils.DrawBorderString(sb, "Y:", new Vector2(panel.X + Pad + 110, y + 4), Color.White, 0.7f);
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + 130, y, 80, 24), 33, bookmarkYInput, mouse, "Y");
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + 220, y, 170, 24), 34, bookmarkNameInput, mouse, "Nombre del marcador");
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 400, y, 205, 24), "Guardar coordenadas", mouse, new Color(45, 105, 75)))
            SaveTeleportBookmark();
        y += 31;

        int visibleCount = 3;
        for (int i = 0; i < visibleCount; i++)
        {
            int idx = i + bookmarkPage * visibleCount;
            TeleportBookmark b = teleportBookmarks[idx];
            int rowY = y + i * 28;
            string label = b.Valid ? $"{idx + 1}. {b.Name}  ({b.X}, {b.Y})" : $"{idx + 1}. (vacío)";
            if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, rowY, 365, 24), label, mouse,
                selectedBookmark == idx ? new Color(65, 90, 125) : new Color(42, 47, 57)))
            {
                selectedBookmark = idx;
                if (b.Valid)
                {
                    bookmarkXInput = b.X.ToString();
                    bookmarkYInput = b.Y.ToString();
                    bookmarkNameInput = b.Name;
                }
            }
            if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 373, rowY, 110, 24), "Ir", mouse, new Color(45, 105, 75)))
                GoToTeleportBookmark(idx);
            if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 491, rowY, 114, 24), "Eliminar", mouse, new Color(110, 55, 55)))
            {
                teleportBookmarks[idx] = new TeleportBookmark(false, "", 0, 0);
                if (selectedBookmark == idx) selectedBookmark = -1;
                SetStatus($"Marcador {idx + 1} eliminado.");
            }
        }
        y += visibleCount * 28 + 5;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, 160, 23), "Marcadores 1-3", mouse))
            bookmarkPage = 0;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 168, y, 160, 23), "Marcadores 4-6", mouse))
            bookmarkPage = 1;
        Utils.DrawBorderString(sb, "Las coordenadas son tiles del mundo; multijugador puede requerir permisos del servidor.",
            new Vector2(panel.X + Pad, y + 27), new Color(235, 195, 120), 0.62f);
    }

    private static void SavePlayerProfile(int index)
    {
        if (index < 0 || index >= profileSlots.Length)
        {
            SetStatus("Espacio de perfil no válido.");
            return;
        }

        Player p = Main.LocalPlayer;
        PlayerProfileSnapshot snapshot = profileSlots[index];
        snapshot.Valid = true;
        snapshot.Name = string.IsNullOrWhiteSpace(profileNameInput) ? $"Perfil {index + 1}" : profileNameInput.Trim();
        snapshot.Life = p.statLife;
        snapshot.MaxLife = p.statLifeMax;
        snapshot.Mana = p.statMana;
        snapshot.MaxMana = p.statManaMax;
        snapshot.SelectedSlot = p.selectedItem;
        snapshot.Defense = p.statDefense;
        snapshot.TileX = (int)(p.position.X / 16f);
        snapshot.TileY = (int)(p.position.Y / 16f);
        snapshot.MoveSpeed = p.moveSpeed;
        snapshot.RunAcceleration = p.runAcceleration;
        snapshot.MaxRunSpeed = p.maxRunSpeed;
        snapshot.NoKnockback = p.noKnockback;
        snapshot.FireWalk = p.fireWalk;
        snapshot.LavaImmune = p.lavaImmune;
        snapshot.FallImmune = p.noFallDmg;
        SetStatus($"Perfil '{snapshot.Name}' guardado en el espacio {index + 1} (memoria temporal).");
    }

    private static void RestorePlayerProfile(int index)
    {
        if (index < 0 || index >= profileSlots.Length || !profileSlots[index].Valid)
        {
            SetStatus("Ese espacio de perfil está vacío.");
            return;
        }

        Player p = Main.LocalPlayer;
        PlayerProfileSnapshot snapshot = profileSlots[index];
        p.statLifeMax = snapshot.MaxLife;
        p.statManaMax = snapshot.MaxMana;
        p.statLife = Math.Clamp(snapshot.Life, 1, Math.Max(1, p.statLifeMax2));
        p.statMana = Math.Clamp(snapshot.Mana, 0, Math.Max(0, p.statManaMax2));
        p.selectedItem = Math.Clamp(snapshot.SelectedSlot, 0, p.inventory.Length - 1);
        p.statDefense = snapshot.Defense;
        p.moveSpeed = snapshot.MoveSpeed;
        p.runAcceleration = snapshot.RunAcceleration;
        p.maxRunSpeed = snapshot.MaxRunSpeed;
        p.noKnockback = snapshot.NoKnockback;
        p.fireWalk = snapshot.FireWalk;
        p.lavaImmune = snapshot.LavaImmune;
        p.noFallDmg = snapshot.FallImmune;
        SyncSelf();
        SetStatus($"Perfil '{snapshot.Name}' restaurado localmente. El servidor puede sobrescribir algunos valores.");
    }

    private static void SaveTeleportBookmark()
    {
        if (!int.TryParse(bookmarkXInput, out int x) || !int.TryParse(bookmarkYInput, out int y))
        {
            SetStatus("Escribe coordenadas X e Y enteras.");
            return;
        }

        x = Math.Clamp(x, 0, Math.Max(0, Main.maxTilesX - 1));
        y = Math.Clamp(y, 0, Math.Max(0, Main.maxTilesY - 1));
        int index = selectedBookmark >= 0 && selectedBookmark < teleportBookmarks.Length
            ? selectedBookmark
            : Array.FindIndex(teleportBookmarks, b => !b.Valid);
        if (index < 0)
        {
            SetStatus("No hay espacios vacíos. Selecciona un marcador para reemplazarlo.");
            return;
        }

        string name = string.IsNullOrWhiteSpace(bookmarkNameInput) ? $"Marcador {index + 1}" : bookmarkNameInput.Trim();
        teleportBookmarks[index] = new TeleportBookmark(true, name, x, y);
        selectedBookmark = index;
        SetStatus($"Marcador '{name}' guardado en ({x}, {y}).");
    }

    private static void GoToTeleportBookmark(int index)
    {
        if (index < 0 || index >= teleportBookmarks.Length || !teleportBookmarks[index].Valid)
        {
            SetStatus("Ese marcador está vacío.");
            return;
        }

        TeleportBookmark b = teleportBookmarks[index];
        teleportX = b.X.ToString();
        teleportY = b.Y.ToString();
        TeleportToCoordinates();
        SetStatus($"Solicitado teletransporte a '{b.Name}' ({b.X}, {b.Y}).");
    }
}
