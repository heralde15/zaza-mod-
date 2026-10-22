using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI.Chat;

namespace Terraria.Unified.Features;

/// <summary>
/// HERO's Mod. Se abre/cierra con ~ (tilde) o F8.
///
/// - Un jugador (netMode 0): las acciones se aplican directamente al juego (igual que antes).
/// - Servidor, modo COMANDOS: las acciones se envian como comandos de chat a TShock, que decide
///   segun los permisos. Las acciones solo-cliente (vuelo, godmode local...) quedan apagadas.
/// - Servidor, modo DIRECTO (vanilla / anarquicos): el cliente actua por si mismo (vuelo, godmode,
///   items, jefes, teleports, butcher...) y sincroniza vida/buffs. Hora, clima y moderar a otros
///   jugadores NO se pueden hacer asi: necesitan TShock (modo COMANDOS) o ser el host.
///   El boton de arriba cambia de modo.
/// </summary>
internal static class HerosModPanel
{
    private enum Tab { Jugador, Mundo, Items, Jefes, Teleport, Jugadores, Info, Inventario, Guia }

    private const int Width = 640;
    private const int Height = 540;
    private const int Pad = 10;
    private const int RowH = 26;

    public static bool Visible { get; set; }

    private enum ServerMode { Comandos, Directo }

    /// <summary>
    /// Comandos = mandar comandos de chat a TShock (el servidor decide segun tus permisos).
    /// Directo  = el cliente actua por si mismo (servidores vanilla / anarquicos).
    /// </summary>
    private static ServerMode serverMode = ServerMode.Comandos;

    private static bool InMultiplayer => Main.netMode == 1;

    /// <summary>true = en un servidor y usando comandos de TShock.</summary>
    private static bool IsServer => Main.netMode == 1 && serverMode == ServerMode.Comandos;

    private static Tab currentTab = Tab.Jugador;
    private static Vector2 position = new(120f, 80f);
    private static bool dragging;
    private static Vector2 dragOffset;

    private static bool godmodeEnabled;
    private static bool noSpawnsEnabled;
    private static bool flyEnabled;
    private const float FlySpeed = 12f;

    private static int activeField;
    private static string itemQuery = "";
    private static string itemAmount = "1";
    private static string itemPrefix = "-1";

    private static string statusMessage = "HERO's Mod listo.";
    private static int statusTimer;

    // ---- Navegador de items (pestana Items) ----
    private const int CellSize = 40;
    private const int BarW = 12;
    private static string[] itemNames;
    private static List<int> allItemIds;
    private static readonly List<int> browserIds = new();
    private static string browserFilterKey;
    private static float browserScroll;
    private static bool browserDragging;
    private static float browserDragStartY;
    private static float browserDragStartScroll;
    private static string hoverTip;

    // Portapapeles local: guarda una copia de los objetos del inventario para pegarla
    // Ãºnicamente en espacios vacÃ­os. No sobrescribe objetos existentes.
    private static Item[] inventoryClipboard;
    private static string inventoryClipboardOwner = "";
    private static int inventoryPage;

    private static bool Clicked => Main.mouseLeft && Main.mouseLeftRelease;

    public static void UpdateLogic()
    {
        if (Main.dedServ || Main.gameMenu)
            return;

        var local = Main.LocalPlayer;

        // En servidor las funciones solo-cliente quedan apagadas (el servidor manda).
        if (IsServer && (godmodeEnabled || flyEnabled))
        {
            godmodeEnabled = false;
            flyEnabled = false;
            local.creativeGodMode = false;
        }

        if (godmodeEnabled && local.active)
        {
            local.creativeGodMode = true;
            for (int i = 0; i < local.buffType.Length; i++)
            {
                if (local.buffType[i] > 0 && Main.debuff[local.buffType[i]])
                {
                    local.DelBuff(i);
                }
            }
        }
        else if (local.active)
        {
            local.creativeGodMode = false;
        }

        if (flyEnabled && local.active && activeField == 0 && !Main.drawingPlayerChat)
        {
            var ks = Main.keyState;
            Vector2 dir = Vector2.Zero;
            if (ks.IsKeyDown(Keys.W) || ks.IsKeyDown(Keys.Up)) dir.Y -= 1f;
            if (ks.IsKeyDown(Keys.S) || ks.IsKeyDown(Keys.Down)) dir.Y += 1f;
            if (ks.IsKeyDown(Keys.A) || ks.IsKeyDown(Keys.Left)) dir.X -= 1f;
            if (ks.IsKeyDown(Keys.D) || ks.IsKeyDown(Keys.Right)) dir.X += 1f;
            if (ks.IsKeyDown(Keys.Space)) dir.Y -= 1f;
            if (ks.IsKeyDown(Keys.LeftShift)) dir.Y += 1f;

            if (dir != Vector2.Zero)
            {
                dir.Normalize();
                local.velocity = dir * FlySpeed;
            }
            else
            {
                local.velocity *= 0.85f;
            }
        }
    }

    public static void Draw(SpriteBatch sb)
    {
        if (Main.dedServ || Main.gameMenu)
            return;

        UpdateLogic();
        HandleToggleKey();

        if (!Visible)
            return;

        hoverTip = null;

        var pixel = TextureAssets.MagicPixel.Value;
        var mouse = new Point(Main.mouseX, Main.mouseY);
        var panelRect = new Rectangle((int)position.X, (int)position.Y, Width, Height);

        if (panelRect.Contains(mouse))
            Main.LocalPlayer.mouseInterface = true;

        var titleRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, 30);
        if (dragging)
        {
            if (Main.mouseLeft)
            {
                position = new Vector2(mouse.X, mouse.Y) - dragOffset;
                position.X = Math.Clamp(position.X, 0f, Math.Max(0f, Main.screenWidth - 100f));
                position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, Main.screenHeight - 60f));
            }
            else
            {
                dragging = false;
            }
        }
        else if (Clicked && titleRect.Contains(mouse))
        {
            dragging = true;
            dragOffset = new Vector2(mouse.X, mouse.Y) - position;
            Main.mouseLeftRelease = false;
        }

        sb.Draw(pixel, panelRect, new Color(30, 33, 40) * 0.98f);
        sb.Draw(pixel, titleRect, new Color(50, 56, 70));
        Utils.DrawBorderString(sb, "HERO's Mod", new Vector2(panelRect.X + Pad, panelRect.Y + 6), Color.Gold, 0.85f);

        string modeText = !InMultiplayer
            ? "Un jugador"
            : (serverMode == ServerMode.Comandos ? "Servidor: COMANDOS (TShock)" : "Servidor: DIRECTO (vanilla/anarquÃ­a)");
        Color modeColor = !InMultiplayer ? new Color(50, 90, 60) : (serverMode == ServerMode.Comandos ? new Color(40, 100, 140) : new Color(150, 90, 30));
        if (DrawButton(sb, pixel, new Rectangle(panelRect.X + 100, panelRect.Y + 5, 260, 20), modeText, mouse, modeColor))
        {
            ToggleServerMode();
        }

        var closeRect = new Rectangle(panelRect.Right - 28, panelRect.Y + 5, 20, 20);
        if (DrawButton(sb, pixel, closeRect, "X", mouse, new Color(180, 50, 50)))
        {
            Visible = false;
            return;
        }

        Tab[] tabs = [Tab.Jugador, Tab.Mundo, Tab.Items, Tab.Jefes, Tab.Teleport,
                      Tab.Jugadores, Tab.Info, Tab.Inventario, Tab.Guia];
        int tabCols = 5;
        int tabWidth = (Width - (Pad * 2) - ((tabCols - 1) * 4)) / tabCols;
        for (int i = 0; i < tabs.Length; i++)
        {
            int row = i / tabCols;
            int col = i % tabCols;
            var tabRect = new Rectangle(panelRect.X + Pad + col * (tabWidth + 4),
                panelRect.Y + 36 + row * 27, tabWidth, 23);
            bool selected = currentTab == tabs[i];
            Color tabColor = selected ? new Color(70, 130, 180) : new Color(45, 50, 60);
            string tabLabel = tabs[i] switch
            {
                Tab.Jugadores => "Jugadores",
                Tab.Inventario => "Inventario",
                Tab.Teleport => "Teleport",
                _ => tabs[i].ToString()
            };
            if (DrawButton(sb, pixel, tabRect, tabLabel, mouse, tabColor) && !selected)
            {
                currentTab = tabs[i];
                activeField = 0;
            }
        }

        int contentY = panelRect.Y + 94;
        switch (currentTab)
        {
            case Tab.Jugador:
                DrawPlayerTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Mundo:
                DrawWorldTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Items:
                DrawItemsTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Jefes:
                DrawBossesTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Teleport:
                DrawTeleportTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Jugadores:
                DrawPlayersTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Info:
                DrawInfoTab(sb, pixel, panelRect, ref contentY);
                break;
            case Tab.Inventario:
                DrawInventoryTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Guia:
                DrawGuideTab(sb, pixel, panelRect, ref contentY);
                break;
        }

        if (statusTimer > 0)
        {
            statusTimer--;
            Utils.DrawBorderString(sb, statusMessage, new Vector2(panelRect.X + Pad, panelRect.Bottom - 24), new Color(150, 255, 150), 0.75f);
        }

        if (activeField != 0)
        {
            HandleTyping();
        }

        // Tooltip al final para que quede encima de todo.
        if (hoverTip != null)
        {
            DrawTooltip(sb, pixel, hoverTip, mouse);
        }
    }

    // ------------------------------------------------------------------ Jugador

    private static void DrawPlayerTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Modificaciones del Jugador");
        int btnW = (Width - (Pad * 2) - 8) / 2;

        string godText = IsServer ? "Godmode (comando)" : (godmodeEnabled ? "Godmode: [ON]" : "Godmode: [OFF]");
        Color godColor = godmodeEnabled ? new Color(40, 140, 60) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), godText, mouse, godColor))
        {
            if (IsServer)
            {
                SendCmd("/godmode");
            }
            else
            {
                godmodeEnabled = !godmodeEnabled;
                Main.LocalPlayer.creativeGodMode = godmodeEnabled;
                if (godmodeEnabled)
                {
                    var p = Main.LocalPlayer;
                    p.statLife = p.statLifeMax2;
                    p.statMana = p.statManaMax2;
                }
                SetStatus(godmodeEnabled ? "Godmode activado (nativo)." : "Godmode desactivado.");
            }
        }

        string flyText = flyEnabled ? "Vuelo Libre: [ON]" : "Vuelo Libre: [OFF]";
        Color flyColor = flyEnabled ? new Color(40, 100, 160) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), flyText, mouse, flyColor))
        {
            if (IsServer)
            {
                NotInServer("Vuelo");
            }
            else
            {
                flyEnabled = !flyEnabled;
                SetStatus(flyEnabled ? "Vuelo: WASD + Espacio (subir) + Shift (bajar)." : "Vuelo desactivado.");
            }
        }
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Curar Full Vida/ManÃ¡", mouse))
        {
            if (IsServer)
            {
                NotInServer("Curar");
            }
            else
            {
                var p = Main.LocalPlayer;
                p.statLife = p.statLifeMax2;
                p.statMana = p.statManaMax2;
                SyncSelf();
                SetStatus("Â¡Salud y manÃ¡ restaurados al mÃ¡ximo!");
            }
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Limpiar Debuffs", mouse))
        {
            if (IsServer)
            {
                NotInServer("Limpiar debuffs");
            }
            else
            {
                var p = Main.LocalPlayer;
                for (int i = 0; i < p.buffType.Length; i++)
                {
                    if (p.buffType[i] > 0 && Main.debuff[p.buffType[i]])
                    {
                        p.DelBuff(i);
                    }
                }
                SyncSelf();
                SetStatus("Efectos negativos eliminados.");
            }
        }
        y += RowH + 6;

        string spawnText = noSpawnsEnabled ? "Spawn Enemigos: [OFF]" : "Spawn Enemigos: [ON]";
        Color spawnColor = noSpawnsEnabled ? new Color(140, 40, 40) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), spawnText, mouse, spawnColor))
        {
            if (InMultiplayer)
            {
                SetStatus("El spawn de enemigos lo controla el servidor (config de TShock).");
            }
            else
            {
                SetStatus("Esta versiÃ³n de Unified no expone la configuraciÃ³n de spawn; no se modificÃ³ el mundo.");
            }
        }
        y += RowH + 14;

        DrawSectionHeader(sb, panel, ref y, "Buffs RÃ¡pidos (10 minutos)");
        (int id, string name)[] buffs =
        {
            (BuffID.Regeneration, "RegeneraciÃ³n"),
            (BuffID.Swiftness, "Velocidad"),
            (BuffID.Ironskin, "Piel de Hierro"),
            (BuffID.ManaRegeneration, "ManÃ¡ Regen"),
            (BuffID.Spelunker, "Spelunker"),
            (BuffID.Shine, "Resplandor"),
        };
        int bw = (Width - (Pad * 2) - 16) / 3;
        for (int i = 0; i < buffs.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            var rect = new Rectangle(panel.X + Pad + col * (bw + 8), y + row * (RowH + 6), bw, RowH);
            if (DrawButton(sb, pixel, rect, buffs[i].name, mouse, new Color(60, 90, 70)))
            {
                if (IsServer)
                {
                    SendCmd($"/buff {buffs[i].id} 600");
                }
                else
                {
                    Main.LocalPlayer.AddBuff(buffs[i].id, 36000);
                    SyncSelf();
                    SetStatus($"Buff aplicado: {buffs[i].name} (10 min)");
                }
            }
        }
        y += 2 * (RowH + 6) + 8;
    }

    // ------------------------------------------------------------------ Mundo

    private static void DrawWorldTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Control de Tiempo y Clima");
        int btnW = (Width - (Pad * 2) - 8) / 2;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Establecer: DÃ­a", mouse))
        {
            if (IsServer)
            {
                SendCmd("/time day");
            }
            else if (InMultiplayer)
            {
                NoRemoteWorld("Hora");
            }
            else
            {
                Main.dayTime = true;
                Main.time = 0;
                SetStatus("Hora cambiada al DÃ­a.");
            }
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Establecer: Noche", mouse))
        {
            if (IsServer)
            {
                SendCmd("/time night");
            }
            else if (InMultiplayer)
            {
                NoRemoteWorld("Hora");
            }
            else
            {
                Main.dayTime = false;
                Main.time = 0;
                SetStatus("Hora cambiada a la Noche.");
            }
        }
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Establecer: MediodÃ­a", mouse))
        {
            if (IsServer)
            {
                SendCmd("/time noon");
            }
            else if (InMultiplayer)
            {
                NoRemoteWorld("Hora");
            }
            else
            {
                Main.dayTime = true;
                Main.time = 27000.0;
                SetStatus("Hora cambiada al MediodÃ­a.");
            }
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Establecer: Medianoche", mouse))
        {
            if (IsServer)
            {
                SendCmd("/time midnight");
            }
            else if (InMultiplayer)
            {
                NoRemoteWorld("Hora");
            }
            else
            {
                Main.dayTime = false;
                Main.time = 16200.0;
                SetStatus("Hora cambiada a la Medianoche.");
            }
        }
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Alternar Lluvia", mouse))
        {
            if (IsServer)
            {
                SendCmd(Main.raining ? "/rain stop" : "/rain start");
            }
            else if (InMultiplayer)
            {
                NoRemoteWorld("Clima");
            }
            else if (Main.raining)
            {
                Main.StopRain();
                SetStatus("Lluvia detenida.");
            }
            else
            {
                Main.StartRain();
                SetStatus("Lluvia iniciada.");
            }
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Matar Enemigos (Butcher)", mouse))
        {
            if (IsServer)
            {
                SendCmd("/butcher");
            }
            else
            {
                int count = 0;
                foreach (var npc in Main.npc)
                {
                    if (npc.active && !npc.friendly && !npc.townNPC && npc.lifeMax > 5)
                    {
                        if (!InMultiplayer)
                        {
                            npc.life = 0;
                            npc.active = false;
                            count++;
                        }
                    }
                }
                SetStatus($"Se eliminaron {count} enemigos activos.");
            }
        }
        y += RowH + 12;
    }

    // ------------------------------------------------------------------ Items

    private static void DrawItemsTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Generador de Objetos (Spawn de Ãtems)");

        EnsureItemNames();
        RefreshBrowser();

        Utils.DrawBorderString(sb, "Nombre o ID del Objeto:", new Vector2(panel.X + Pad, y), Color.White, 0.75f);
        Utils.DrawBorderString(sb, $"{browserIds.Count} resultados", new Vector2(panel.Right - Pad - 110, y), Color.Gray, 0.7f);
        y += 20;

        var queryRect = new Rectangle(panel.X + Pad, y, Width - (Pad * 2), RowH);
        DrawTextField(sb, pixel, queryRect, 1, itemQuery, mouse, "Escribe para buscar (nombre o ID)...");
        y += RowH + 10;

        Utils.DrawBorderString(sb, "Cantidad:", new Vector2(panel.X + Pad, y), Color.White, 0.75f);
        Utils.DrawBorderString(sb, "Prefijo (ID, -1=aleatorio, 0=ninguno):", new Vector2(panel.X + Pad + 110, y), Color.White, 0.75f);
        y += 20;

        var amountRect = new Rectangle(panel.X + Pad, y, 100, RowH);
        DrawTextField(sb, pixel, amountRect, 2, itemAmount, mouse);

        var prefixRect = new Rectangle(panel.X + Pad + 110, y, 100, RowH);
        DrawTextField(sb, pixel, prefixRect, 3, itemPrefix, mouse);
        y += RowH + 10;

        var giveBtnRect = new Rectangle(panel.X + Pad, y, Width - (Pad * 2), RowH);
        if (DrawButton(sb, pixel, giveBtnRect, "Obtener Objeto (primer resultado / nombre escrito)", mouse, new Color(40, 110, 160)))
        {
            SpawnItemAction();
        }
        y += RowH + 12;

        // Navegador con iconos: clic en un item para obtenerlo.
        int browserH = Math.Max(CellSize * 2, panel.Bottom - 30 - y);
        var browserRect = new Rectangle(panel.X + Pad, y, Width - (Pad * 2), browserH);
        DrawItemBrowser(sb, pixel, browserRect, mouse);
    }

    private static void EnsureItemNames()
    {
        if (itemNames != null)
            return;

        itemNames = new string[ItemID.Count];
        allItemIds = new List<int>();
        for (int i = 1; i < ItemID.Count; i++)
        {
            string name = Lang.GetItemNameValue(i);
            itemNames[i] = name ?? "";
            if (!string.IsNullOrWhiteSpace(itemNames[i]))
                allItemIds.Add(i);
        }
    }

    private static void RefreshBrowser()
    {
        string key = (itemQuery ?? "").Trim();
        if (key == browserFilterKey)
            return;

        browserFilterKey = key;
        browserScroll = 0f;
        browserIds.Clear();

        bool isNumber = int.TryParse(key, out int number);
        foreach (int id in allItemIds)
        {
            if (key.Length == 0
                || (isNumber && id == number)
                || itemNames[id].Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                browserIds.Add(id);
            }
        }
    }

    private static void DrawItemBrowser(SpriteBatch sb, Texture2D pixel, Rectangle area, Point mouse)
    {
        sb.Draw(pixel, area, new Color(20, 22, 28));

        int cols = Math.Max(1, (area.Width - BarW - 4) / CellSize);
        int rows = (browserIds.Count + cols - 1) / cols;
        float maxScroll = Math.Max(0f, rows * CellSize + 4 - area.Height);
        bool hoverArea = area.Contains(mouse);

        if (hoverArea)
        {
            int wheel = PlayerInput.ScrollWheelDeltaForUI;
            if (wheel != 0)
            {
                browserScroll -= wheel / 120f * CellSize;
                PlayerInput.ScrollWheelDeltaForUI = 0;
            }
        }

        // Barra de scroll (arrastrable).
        var track = new Rectangle(area.Right - BarW, area.Y, BarW, area.Height);
        int thumbH = maxScroll > 0f ? Math.Max(24, (int)(area.Height * (area.Height / (area.Height + maxScroll)))) : area.Height;

        if (browserDragging)
        {
            if (Main.mouseLeft)
            {
                float trackLen = area.Height - thumbH;
                if (trackLen > 0f)
                    browserScroll = browserDragStartScroll + (Main.mouseY - browserDragStartY) * maxScroll / trackLen;
            }
            else
            {
                browserDragging = false;
            }
        }
        else if (Clicked && maxScroll > 0f && track.Contains(mouse))
        {
            browserDragging = true;
            browserDragStartY = Main.mouseY;
            browserDragStartScroll = browserScroll;
            Main.mouseLeftRelease = false;
        }

        browserScroll = Math.Clamp(browserScroll, 0f, maxScroll);

        int firstRow = (int)(browserScroll / CellSize);
        int lastRow = (int)((browserScroll + area.Height) / CellSize);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int idx = row * cols + col;
                if (idx >= browserIds.Count)
                    break;

                var cell = new Rectangle(area.X + 2 + col * CellSize, area.Y + 2 + (int)(row * CellSize - browserScroll), CellSize - 2, CellSize - 2);

                // Solo se dibujan celdas completas dentro del area (sin recorte).
                if (cell.Y < area.Y || cell.Bottom > area.Bottom)
                    continue;

                int id = browserIds[idx];
                bool hover = hoverArea && !browserDragging && mouse.X < area.Right - BarW && cell.Contains(mouse);

                sb.Draw(pixel, cell, hover ? new Color(75, 85, 110) : new Color(50, 55, 68));
                DrawItemIcon(sb, id, cell);

                if (hover)
                {
                    hoverTip = $"{itemNames[id]} (ID {id})";
                    if (Clicked)
                    {
                        Main.mouseLeftRelease = false;
                        GiveItemById(id);
                    }
                }
            }
        }

        if (maxScroll > 0f)
        {
            sb.Draw(pixel, track, Color.Black * 0.4f);
            int thumbY = area.Y + (int)((area.Height - thumbH) * (browserScroll / maxScroll));
            sb.Draw(pixel, new Rectangle(track.X + 2, thumbY, BarW - 4, thumbH), browserDragging ? new Color(110, 130, 190) : new Color(75, 85, 110));
        }

        if (browserIds.Count == 0)
        {
            Utils.DrawBorderString(sb, "Sin resultados", new Vector2(area.X + 10, area.Y + 10), Color.Gray, 0.8f);
        }
    }

    private static void DrawItemIcon(SpriteBatch sb, int id, Rectangle cell)
    {
        Main.instance.LoadItem(id);
        var tex = TextureAssets.Item[id].Value;
        if (tex == null)
            return;

        var anim = Main.itemAnimations[id];
        var frame = anim != null ? anim.GetFrame(tex) : new Rectangle(0, 0, tex.Width, tex.Height);

        float scale = Math.Min(1f, 30f / Math.Max(frame.Width, frame.Height));
        var center = new Vector2(cell.X + cell.Width / 2f, cell.Y + cell.Height / 2f);
        var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);

        sb.Draw(tex, center, frame, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
    }

    private static void DrawTooltip(SpriteBatch sb, Texture2D pixel, string text, Point mouse)
    {
        var font = FontAssets.MouseText.Value;
        float scale = 0.8f;
        var size = font.MeasureString(text) * scale;
        int w = (int)size.X + 12;
        int h = (int)size.Y + 8;
        int x = Math.Min(mouse.X + 16, Main.screenWidth - w - 4);
        int ty = Math.Min(mouse.Y + 16, Main.screenHeight - h - 4);

        sb.Draw(pixel, new Rectangle(x, ty, w, h), new Color(10, 12, 20) * 0.95f);
        Utils.DrawBorderString(sb, text, new Vector2(x + 6, ty + 4), Color.White, scale);
    }

    // ------------------------------------------------------------------ Jefes

    private static void DrawBossesTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Invocador RÃ¡pido de Jefes (Bosses)");
        int btnW = (Width - (Pad * 2) - 8) / 2;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Rey Slime", mouse))
            SpawnBoss(50, "Rey Slime", "king");

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Ojo de Cthulhu", mouse))
            SpawnBoss(4, "Ojo de Cthulhu", "eye");
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Devoramundos", mouse))
            SpawnBoss(13, "Devoramundos", "eater");

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Skeletron", mouse))
            SpawnBoss(35, "Skeletron", "skeletron");
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Plantera", mouse))
            SpawnBoss(262, "Plantera", "plantera");

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Golem", mouse))
            SpawnBoss(245, "Golem", "golem");
        y += RowH + 10;

        DrawSectionHeader(sb, panel, ref y, "Progreso de jefes del mundo");
        DrawBossState(sb, panel, ref y, "Rey Slime", NPC.downedSlimeKing);
        DrawBossState(sb, panel, ref y, "Ojo de Cthulhu", NPC.downedBoss1);
        DrawBossState(sb, panel, ref y, "Devoramundos / Cerebro", NPC.downedBoss2);
        DrawBossState(sb, panel, ref y, "EsqueletrÃ³n", NPC.downedBoss3);
        DrawBossState(sb, panel, ref y, "Abeja Reina", NPC.downedQueenBee);
        DrawBossState(sb, panel, ref y, "Jefes mecÃ¡nicos", NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3);
        DrawBossState(sb, panel, ref y, "Plantera", NPC.downedPlantBoss);
        DrawBossState(sb, panel, ref y, "Golem", NPC.downedGolemBoss);
        DrawBossState(sb, panel, ref y, "SeÃ±or de la Luna", NPC.downedMoonlord);
    }

    private static void DrawBossState(SpriteBatch sb, Rectangle panel, ref int y, string name, bool defeated)
    {
        if (y > panel.Bottom - 36)
            return;
        Color stateColor = defeated ? new Color(115, 220, 130) : new Color(230, 175, 100);
        string state = defeated ? "Derrotado" : "Pendiente";
        Utils.DrawBorderString(sb, name, new Vector2(panel.X + Pad, y), Color.White, 0.65f);
        Utils.DrawBorderString(sb, state, new Vector2(panel.X + Width - 112, y), stateColor, 0.65f);
        y += 18;
    }

    // ------------------------------------------------------------------ Teleport

    private static void DrawTeleportTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Puntos de Teletransporte");
        int btnW = (Width - (Pad * 2) - 8) / 2;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Spawn del Mundo", mouse))
        {
            if (IsServer)
                SendCmd("/spawn");
            else
                TeleportPlayer(Main.spawnTileX * 16, Main.spawnTileY * 16, "Spawn");
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Inframundo (Hell)", mouse))
        {
            if (IsServer)
                SendCmd($"/tppos {Main.maxTilesX / 2} {Main.maxTilesY - 120}");
            else
                TeleportPlayer(Main.maxTilesX / 2 * 16, (Main.maxTilesY - 120) * 16, "Inframundo");
        }
        y += RowH + 6;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Mazmorra (Dungeon)", mouse))
        {
            if (IsServer)
                SendCmd($"/tppos {Main.dungeonX} {Main.dungeonY}");
            else
                TeleportPlayer(Main.dungeonX * 16, Main.dungeonY * 16, "Mazmorra");
        }
        y += RowH + 12;
    }

    // ------------------------------------------------------------------ Jugadores

    private static Rectangle Slot(int startX, int rowY, int w, int gap, int k)
    {
        return new Rectangle(startX + k * (w + gap), rowY, w, RowH);
    }

    private static void DrawPlayersTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Jugadores Conectados (ModeraciÃ³n)");

        int nameW = 120;
        int gap = 4;
        int actionW = (Width - (Pad * 2) - nameW - gap * 5) / 6;
        int startX = panel.X + Pad + nameW;
        int shown = 0;

        for (int i = 0; i < Main.player.Length; i++)
        {
            var target = Main.player[i];
            if (target == null || !target.active)
                continue;

            if (y > panel.Bottom - RowH - 40)
                break;

            bool isSelf = i == Main.myPlayer;
            string label = isSelf ? $"{target.name} (TÃº)" : target.name;
            Utils.DrawBorderString(sb, label, new Vector2(panel.X + Pad, y + 5), Color.White, 0.7f);

            if (!isSelf)
            {
                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 0), "Ir", mouse, new Color(40, 100, 140)))
                {
                    if (IsServer)
                        SendCmd($"/tp {Quote(target.name)}");
                    else
                        TeleportToPlayer(target);
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 1), "Traer", mouse, new Color(40, 100, 140)))
                {
                    if (IsServer)
                        SendCmd($"/tphere {Quote(target.name)}");
                    else if (InMultiplayer)
                        NeedsCommands("Traer");
                    else
                        TeleportToMe(target);
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 2), "Sanar", mouse, new Color(40, 140, 70)))
                {
                    if (InMultiplayer)
                        NeedsCommands("Sanar a otro jugador");
                    else
                        HealPlayer(target);
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 3), "Matar", mouse, new Color(150, 40, 40)))
                {
                    if (IsServer)
                        SendCmd($"/kill {Quote(target.name)}");
                    else if (InMultiplayer)
                        NeedsCommands("Matar");
                    else
                        KillPlayerAction(target);
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 4), "Kick", mouse, new Color(160, 90, 30)))
                {
                    if (IsServer)
                        SendCmd($"/kick {Quote(target.name)}");
                    else
                        NeedsCommands("Kick");
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 5), "Mute", mouse, new Color(110, 70, 140)))
                {
                    if (IsServer)
                        SendCmd($"/mute {Quote(target.name)}");
                    else
                        NeedsCommands("Mute");
                }
            }

            y += RowH + 6;
            shown++;
        }

        if (shown == 0)
        {
            Utils.DrawBorderString(sb, "No hay jugadores conectados.", new Vector2(panel.X + Pad, y + 5), Color.Gray, 0.75f);
            y += RowH;
        }

        y += 8;
        string note = IsServer
            ? "Modo COMANDOS: todo va por comandos de TShock (segun tus permisos). Ban: usa /ban en el chat."
            : (InMultiplayer
                ? "Modo DIRECTO: solo 'Ir'. Traer/Matar/Kick/Mute necesitan TShock (modo COMANDOS)."
                : "Kick/Mute/Ir/Traer solo aplican en servidor.");
        Utils.DrawBorderString(sb, note, new Vector2(panel.X + Pad, panel.Bottom - 46), new Color(200, 180, 100), 0.65f);
    }


    // ------------------------------------------------------------------ Informacion del mundo y jugador

    private static void DrawInfoTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y)
    {
        var p = Main.LocalPlayer;
        DrawSectionHeader(sb, panel, ref y, "Resumen del mundo");
        string mode = Main.masterMode ? "Maestro" : Main.expertMode ? "Experto" : "Normal";
        DrawInfoLine(sb, panel, ref y, "Mundo", string.IsNullOrWhiteSpace(Main.worldName) ? "(sin nombre)" : Main.worldName);
        DrawInfoLine(sb, panel, ref y, "TamaÃ±o", $"{Main.maxTilesX} x {Main.maxTilesY} tiles");
        DrawInfoLine(sb, panel, ref y, "Dificultad", mode);
        DrawInfoLine(sb, panel, ref y, "Hardmode", Main.hardMode ? "Activado" : "No activado");
        DrawInfoLine(sb, panel, ref y, "Hora", Main.dayTime ? "DÃ­a" : "Noche");
        DrawInfoLine(sb, panel, ref y, "Clima", Main.raining ? "Lluvia" : "Sin lluvia");
        y += 5;
        DrawSectionHeader(sb, panel, ref y, "Tu personaje");
        DrawInfoLine(sb, panel, ref y, "Vida", $"{p.statLife}/{p.statLifeMax2}");
        DrawInfoLine(sb, panel, ref y, "ManÃ¡", $"{p.statMana}/{p.statManaMax2}");
        DrawInfoLine(sb, panel, ref y, "Defensa", p.statDefense.ToString());
        DrawInfoLine(sb, panel, ref y, "PosiciÃ³n", $"X {(int)(p.position.X / 16f)}  Y {(int)(p.position.Y / 16f)}");
        DrawInfoLine(sb, panel, ref y, "Bioma", p.ZoneDungeon ? "Mazmorra" : p.ZoneUnderworldHeight ? "Inframundo" :
            p.ZoneJungle ? "Jungla" : p.ZoneSnow ? "Nieve" : p.ZoneDesert ? "Desierto" :
            p.ZoneBeach ? "Playa" : p.ZoneCorrupt ? "CorrupciÃ³n" : p.ZoneCrimson ? "CarmesÃ­" :
            p.ZoneHallow ? "Sagrado" : (p.position.Y / 16f < Main.worldSurface ? "Superficie/otro" : "Subsuelo/otro"));
        Utils.DrawBorderString(sb, "Los datos mostrados son locales; el servidor controla el estado autoritativo.",
            new Vector2(panel.X + Pad, panel.Bottom - 48), new Color(220, 190, 120), 0.62f);
    }

    private static void DrawInfoLine(SpriteBatch sb, Rectangle panel, ref int y, string label, string value)
    {
        Utils.DrawBorderString(sb, label + ":", new Vector2(panel.X + Pad, y), new Color(155, 190, 230), 0.72f);
        Utils.DrawBorderString(sb, value ?? "-", new Vector2(panel.X + 150, y), Color.White, 0.72f);
        y += 20;
    }

    // ------------------------------------------------------------------ Inventario: inspeccion y portapapeles

    private static void DrawInventoryTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        var p = Main.LocalPlayer;
        DrawSectionHeader(sb, panel, ref y, "Inventario del personaje local");
        int half = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, half, RowH), "Copiar inventario", mouse,
            new Color(45, 105, 135)))
        {
            inventoryClipboard = new Item[p.inventory.Length];
            for (int i = 0; i < p.inventory.Length; i++)
                inventoryClipboard[i] = p.inventory[i]?.Clone() ?? new Item();
            inventoryClipboardOwner = p.name ?? "Jugador";
            SetStatus($"Inventario copiado: {inventoryClipboardOwner}.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + half + 8, y, half, RowH), "Pegar en huecos", mouse,
            new Color(65, 115, 75)))
        {
            if (inventoryClipboard == null)
            {
                SetStatus("Primero copia un inventario.");
            }
            else
            {
                int pasted = 0;
                int n = Math.Min(p.inventory.Length, inventoryClipboard.Length);
                for (int i = 0; i < n; i++)
                {
                    if (p.inventory[i] == null || p.inventory[i].IsAir)
                    {
                        if (inventoryClipboard[i] != null && !inventoryClipboard[i].IsAir)
                        {
                            p.inventory[i] = inventoryClipboard[i].Clone();
                            pasted++;
                        }
                    }
                }
                if (pasted > 0)
                    SyncSelf();
                SetStatus($"Pegados {pasted} espacios del inventario copiado. No se sobrescribieron objetos.");
            }
        }
        y += RowH + 6;
        DrawInfoLine(sb, panel, ref y, "Portapapeles", inventoryClipboard == null ? "VacÃ­o" : inventoryClipboardOwner);
        DrawInfoLine(sb, panel, ref y, "Regla", "Solo pega en espacios vacÃ­os");
        y += 3;
        DrawSectionHeader(sb, panel, ref y, "Contenido (mochila)");
        int first = inventoryPage * 16;
        int last = Math.Min(p.inventory.Length, first + 16);
        int leftX = panel.X + Pad;
        int rightX = panel.X + Width / 2;
        for (int i = first; i < last; i++)
        {
            Item item = p.inventory[i];
            string itemName = item == null || item.IsAir ? "(vacÃ­o)" : item.Name;
            string stack = item == null || item.IsAir ? "" : $" x{item.stack}";
            int col = (i - first) / 8;
            int row = (i - first) % 8;
            int x = col == 0 ? leftX : rightX;
            int yy = y + row * 22;
            Utils.DrawBorderString(sb, $"{i + 1}. {itemName}{stack}",
                new Vector2(x, yy), item == null || item.IsAir ? Color.Gray : Color.White, 0.64f);
        }
        int navY = panel.Bottom - 76;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, navY, 120, 22), "Anterior", mouse))
            inventoryPage = Math.Max(0, inventoryPage - 1);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 128, navY, 120, 22), "Siguiente", mouse))
            inventoryPage = Math.Min(Math.Max(0, (p.inventory.Length - 1) / 16), inventoryPage + 1);
        Utils.DrawBorderString(sb, $"PÃ¡gina {inventoryPage + 1}", new Vector2(panel.X + Pad + 260, navY + 4),
            Color.LightGray, 0.7f);
        Utils.DrawBorderString(sb, "La copia es local y no lee el inventario privado de otros jugadores.",
            new Vector2(panel.X + Pad, panel.Bottom - 46), new Color(220, 190, 120), 0.62f);
    }

    // ------------------------------------------------------------------ Guia rapida

    private static void DrawGuideTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y)
    {
        DrawSectionHeader(sb, panel, ref y, "GuÃ­a rÃ¡pida: crafteos, buffs y progresiÃ³n");
        string[] tips =
        {
            "Crafteos: consulta el objeto en el menÃº de fabricaciÃ³n del juego",
            "y acÃ©rcate a la estaciÃ³n requerida para ver recetas disponibles.",
            "Materiales: guarda minerales, barras, gemas, telaraÃ±as y almas;",
            "muchas recetas dependen del progreso del mundo.",
            "",
            "BUFFS ÃšTILES",
            "RegeneraciÃ³n â€” recuperaciÃ³n de vida.",
            "Piel de hierro â€” aumenta la defensa.",
            "Velocidad â€” aumenta la velocidad de movimiento.",
            "RegeneraciÃ³n de manÃ¡ â€” acelera la recuperaciÃ³n de manÃ¡.",
            "EspeleÃ³logo â€” resalta minerales y tesoros.",
            "Resplandor â€” ilumina el entorno.",
            "",
            "PROGRESIÃ“N",
            "Pre-Hardmode: Rey Slime, Ojo de Cthulhu, jefes malignos,",
            "Abeja Reina, EsqueletrÃ³n y Muro de Carne.",
            "Hardmode: jefes mecÃ¡nicos, Plantera, Golem y eventos finales.",
            "",
            "Los drops y recetas pueden variar segÃºn versiÃ³n/mods.",
            "Esta pestaÃ±a es informativa; no altera el mundo."
        };
        foreach (string tip in tips)
        {
            if (y > panel.Bottom - 40)
                break;
            Color color = tip == "BUFFS ÃšTILES" || tip == "PROGRESIÃ“N"
                ? new Color(130, 180, 240) : (string.IsNullOrEmpty(tip) ? Color.Gray : Color.White);
            Utils.DrawBorderString(sb, tip, new Vector2(panel.X + Pad, y), color, 0.68f);
            y += 19;
        }
    }

    // ------------------------------------------------------------------ dibujo comun

    private static void DrawSectionHeader(SpriteBatch sb, Rectangle panel, ref int y, string title)
    {
        Utils.DrawBorderString(sb, title, new Vector2(panel.X + Pad, y), new Color(130, 180, 240), 0.8f);
        y += 22;
    }

    private static bool DrawButton(SpriteBatch sb, Texture2D pixel, Rectangle rect, string text, Point mouse, Color? customBg = null)
    {
        bool hover = rect.Contains(mouse);
        Color bg = hover ? new Color(75, 85, 110) : (customBg ?? new Color(50, 55, 68));
        sb.Draw(pixel, rect, bg);

        var font = FontAssets.MouseText.Value;
        float scale = 0.7f;
        var size = font.MeasureString(text) * scale;
        Vector2 textPos = new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f + 2f);
        Utils.DrawBorderString(sb, text, textPos, Color.White, scale);

        if (hover && Clicked)
        {
            Main.mouseLeftRelease = false;
            activeField = 0;
            return true;
        }
        return false;
    }

    private static void DrawTextField(SpriteBatch sb, Texture2D pixel, Rectangle rect, int fieldId, string text, Point mouse, string hint = null)
    {
        bool active = activeField == fieldId;
        sb.Draw(pixel, rect, active ? new Color(40, 48, 65) : new Color(20, 22, 28));

        string display = text;
        if (active && (Main.timeForVisualEffects % 60 < 30))
            display += "|";

        if (hint != null && string.IsNullOrEmpty(text) && !active)
            Utils.DrawBorderString(sb, hint, new Vector2(rect.X + 6, rect.Y + 4), Color.Gray, 0.8f);
        else
            Utils.DrawBorderString(sb, display, new Vector2(rect.X + 6, rect.Y + 4), Color.White, 0.8f);

        if (rect.Contains(mouse) && Clicked)
        {
            Main.mouseLeftRelease = false;
            activeField = fieldId;
            Main.clrInput();
        }
    }

    private static void HandleToggleKey()
    {
        if (Main.drawingPlayerChat || activeField != 0)
            return;

        bool tilde = Main.keyState.IsKeyDown(Keys.OemTilde) && Main.oldKeyState.IsKeyUp(Keys.OemTilde);
        bool f8 = Main.keyState.IsKeyDown(Keys.F8) && Main.oldKeyState.IsKeyUp(Keys.F8);
        if (tilde || f8)
        {
            Visible = !Visible;
        }
    }

    private static void HandleTyping()
    {
        PlayerInput.WritingText = true;
        Main.instance.HandleIME();

        if (activeField == 1)
            itemQuery = Main.GetInputText(itemQuery);
        else if (activeField == 2)
            itemAmount = Main.GetInputText(itemAmount);
        else if (activeField == 3)
            itemPrefix = Main.GetInputText(itemPrefix);

        if (Main.inputTextEnter || Main.inputTextEscape)
        {
            activeField = 0;
            Main.clrInput();
        }
    }

    // ------------------------------------------------------------------ servidor

    private static string Quote(string name)
    {
        return "\"" + (name ?? "").Replace("\"", "") + "\"";
    }

    private static void SendCmd(string command)
    {
        if (Main.netMode != 1)
        {
            SetStatus("Sin conexiÃ³n a un servidor.");
            return;
        }

        ChatHelper.SendChatMessageFromClient(ChatManager.Commands.CreateOutgoingMessage(command));
        SetStatus($"Comando enviado: {command}");
    }

    /// <summary>Accion solo-cliente bloqueada en modo Comandos (para respetar al servidor).</summary>
    private static void NotInServer(string what)
    {
        SetStatus($"{what}: solo en un jugador o en modo DIRECTO (arriba).");
    }

    /// <summary>Accion que en servidor solo existe como comando de TShock.</summary>
    private static void NeedsCommands(string what)
    {
        SetStatus($"{what}: solo con TShock. Cambia a modo COMANDOS (arriba).");
    }

    /// <summary>Hora/clima: un cliente no puede cambiarlos en un servidor sin comandos.</summary>
    private static void NoRemoteWorld(string what)
    {
        SetStatus($"{what}: un servidor vanilla no lo permite desde el cliente. Usa TShock (modo COMANDOS) o el host.");
    }

    private static void ToggleServerMode()
    {
        if (!InMultiplayer)
        {
            SetStatus("El modo de servidor se usa al entrar a un servidor.");
            return;
        }

        if (serverMode == ServerMode.Comandos)
        {
            serverMode = ServerMode.Directo;
            SetStatus("Modo DIRECTO: acciones del cliente. Con anticheat (TShock) pueden kickearte; Ãºsalo donde estÃ© permitido.");
        }
        else
        {
            serverMode = ServerMode.Comandos;
            SetStatus("Modo COMANDOS: todo va por comandos de TShock.");
        }
    }

    /// <summary>Sincroniza vida, mana y buffs propios con el servidor (solo multijugador).</summary>
    private static void SyncSelf()
    {
        if (Main.netMode != 1)
            return;

        NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, Main.myPlayer);
                NetMessage.SendData(MessageID.PlayerBuffs, -1, -1, null, Main.myPlayer);
    }

    // ------------------------------------------------------------------ acciones

    private static int ResolveItemId(string query)
    {
        int id = 0;

        if (int.TryParse(query, out int parsedId))
        {
            id = parsedId;
        }
        else if (ItemID.Search.TryGetId(query, out int searchId))
        {
            id = searchId;
        }
        else
        {
            for (int i = 1; i < ItemID.Count; i++)
            {
                string name = Lang.GetItemNameValue(i);
                if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    id = i;
                    break;
                }
            }
        }

        return id;
    }

    private static void SpawnItemAction()
    {
        if (string.IsNullOrWhiteSpace(itemQuery)) return;

        int id = ResolveItemId(itemQuery);

        if (id > 0 && id < ItemID.Count)
        {
            GiveItemById(id);
        }
        else
        {
            SetStatus($"No se encontrÃ³ el objeto: '{itemQuery}'");
        }
    }

    private static void GiveItemById(int id)
    {
        if (id <= 0 || id >= ItemID.Count)
            return;

        int count = int.TryParse(itemAmount, out int parsedCount) && parsedCount > 0 ? Math.Min(parsedCount, 9999) : 1;
        int prefixValue = int.TryParse(itemPrefix, out int parsedPrefix) ? parsedPrefix : -1;

        if (IsServer)
        {
            // TShock: /item <id> <cantidad> [prefijo]
            string cmd = $"/item {id} {count}";
            if (prefixValue > 0)
                cmd += $" {prefixValue}";

            SendCmd(cmd);
            return;
        }

        var p = Main.LocalPlayer;
        Item.NewItem(new EntitySource_WorldEvent(), (int)p.position.X, (int)p.position.Y, p.width, p.height, id, count, false, prefixValue);
        SetStatus($"Objeto entregado: {Lang.GetItemNameValue(id)} (x{count})");
    }

    private static void SpawnBoss(int npcId, string bossName, string tshockName)
    {
        if (IsServer)
        {
            SendCmd($"/spawnboss {tshockName}");
            return;
        }

        NPC.SpawnOnPlayer(Main.myPlayer, npcId);
        SetStatus($"Â¡{bossName} ha sido invocado!");
    }

    private static void TeleportPlayer(int worldX, int worldY, string destination)
    {
        var p = Main.LocalPlayer;
        p.Teleport(new Vector2(worldX, worldY - (p.height * 16)), 1);
        SetStatus($"Teletransportado a: {destination}");
    }

    private static void TeleportToPlayer(Player target)
    {
        Main.LocalPlayer.Teleport(target.position, 1);
        SetStatus($"Teletransportado hacia {target.name}.");
    }

    private static void TeleportToMe(Player target)
    {
        target.Teleport(Main.LocalPlayer.Center, 1);
        SetStatus($"{target.name} fue traÃ­do hacia ti.");
    }

    private static void HealPlayer(Player target)
    {
        target.statLife = target.statLifeMax2;
        target.statMana = target.statManaMax2;
        SetStatus($"{target.name} fue sanado por completo.");
    }

    private static void KillPlayerAction(Player target)
    {
        target.KillMe(Terraria.DataStructures.PlayerDeathReason.ByCustomReason($"{target.name} fue eliminado por un administrador."), 1000.0, 0);
        SetStatus($"{target.name} fue eliminado.");
    }

    private static void SetStatus(string msg)
    {
        statusMessage = msg;
        statusTimer = 240;
    }
}
