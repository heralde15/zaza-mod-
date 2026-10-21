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
/// HERO's Admin. Panel independiente: se abre/cierra con tecla ` (OemTilde). El botón superior cambia NORMAL / DEBUG / CHEAT.
///
/// - Un jugador (netMode 0): las acciones se aplican directamente al juego (igual que antes).
/// - Las acciones del editor de objetos modifican directamente el Item local y no ejecutan comandos.
/// - En multijugador, los comandos estándar usan el servidor; las herramientas WorldEdit usan /heros.
/// - El manejador HerosModServerCommands debe integrarse en el servidor Unified para WorldEdit remoto.
/// </summary>
internal static class HerosAdminPanel
{
    // Terraria Unified no expone todos los EntitySource_* de tModLoader.
    // Esta fuente mínima satisface Item.NewItem mediante IEntitySource.
    private sealed class PanelItemSource : IEntitySource
    {
        public string Context => "HerosAdminPanel";
    }

    private enum Tab { Jugador, Aventura, WorldEdit, Mundo, Editor, Buffs, Jefes, NPCs, Proyectiles, Teleport, Jugadores, Permisos, Configuracion }

    // Subvistas dentro de una sola pestaña Aventura: no crean paneles independientes.
    private enum AdventureView { Perfil, Items }
    private static AdventureView adventureView = AdventureView.Perfil;

    private const int BaseWidth = 680;
    private const int BaseHeight = 720;
    // Tamaño configurable desde la pestaña Configuración.
    private static float uiScale = 1.0f;
    private static int Width => (int)(BaseWidth * uiScale);
    private static int Height => (int)(BaseHeight * uiScale);
    private static int themeIndex;
    private static readonly Color[] themeColors =
    {
        new Color(70, 130, 180), // azul
        new Color(55, 155, 105), // verde
        new Color(155, 95, 200), // violeta
        new Color(210, 115, 55), // naranja
        new Color(205, 70, 95)   // rojo
    };
    private static Color AccentColor => themeColors[Math.Clamp(themeIndex, 0, themeColors.Length - 1)];
    private const int Pad = 10;
    private const int RowH = 26;

    public static bool Visible { get; set; }

    private enum ServerMode { Directo }

    // El transporte del servidor se mantiene nativo; este selector cambia el perfil del panel.
    private static ServerMode serverMode = ServerMode.Directo;

    private enum ToolMode { Normal, Debug, Cheat }
    private static ToolMode toolMode = ToolMode.Normal;

    private static bool InMultiplayer => Main.netMode == 1;

    /// <summary>true = conectado a un servidor remoto; las acciones administrativas usan comandos TShock.</summary>
    private static bool IsServer => Main.netMode == 1;

    private static Tab currentTab = Tab.Jugador;
    private static Vector2 position = new(24f, 36f);
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
    private static string itemDamageText = "0";
    private static string itemDefenseText = "0";
    // Editor del objeto que ya está en el inventario del jugador.
    private static string editorSlotText = "0";
    private static string editorStackText = "1";
    private static string editorDamageText = "0";
    private static string editorDefenseText = "0";
    private static string editorCritText = "0";
    private static string editorRarityText = "0";
    private static string editorKnockbackText = "0";
    private static string editorPrefixText = "0";
    // Propiedades avanzadas del objeto.
    private static string editorUseTimeText = "0";
    private static string editorUseAnimationText = "0";
    private static string editorUseStyleText = "0";
    private static string editorManaText = "0";
    private static string editorShootText = "0";
    private static string editorShootSpeedText = "0";
    private static string editorScaleText = "1";
    private static string editorValueText = "0";
    private static bool editorAutoReuse;
    private static bool editorConsumable;
    private static int editorLoadedSlot = -1;
    private static bool editorAdvancedPage;
    private static bool editorExtraPage;
    // Editor de vida y maná del jugador local.
    private static string playerLifeText = "100";
    private static string playerMaxLifeText = "100";
    private static string playerManaText = "20";
    private static string playerMaxManaText = "20";
    private static string buffIdText = "1";
    private static string buffDurationText = "600";
    private static string npcQuery = "";
    private static string npcAmountText = "1";
    private static string editorPickText = "0";
    private static string editorAxeText = "0";
    private static string editorHealLifeText = "0";
    private static string editorHealManaText = "0";
    // Propiedades avanzadas adicionales del objeto.
    private static string editorAmmoText = "0";
    private static string editorUseAmmoText = "0";
    private static string editorCreateTileText = "-1";
    private static string editorCreateWallText = "-1";
    private static string editorBuffTypeText = "0";
    private static string editorBuffTimeText = "0";
    private static string editorMountTypeText = "-1";
    private static bool editorUseTurn;
    private static bool editorNoMelee;
    private static bool editorNoUseGraphic;

    // Mapa de teletransporte cacheado; se actualiza con el botón Recargar.
    private const int MapTextureWidth = 360;
    private const int MapTextureHeight = 100;
    private static Texture2D worldMapTexture;
    private static Color[] worldMapPixels;
    private static bool worldMapNeedsRefresh = true;
    private static bool spanish = true;

    // WorldEdit adaptado a la API nativa de Terraria Unified.
    // Se conservan conceptos de selección/relleno/reemplazo/deshacer de las fuentes de referencia,
    // pero no se copian sus dependencias externas (ModsNetMessages/SkiaSharp) que no existen aquí.
    // Operaciones limitadas a un jugador para no alterar servidores remotos.
    private static bool weHasPos1, weHasPos2;
    private static Point wePos1, wePos2;
    private static bool weDragMode;
    private static bool weDragSelecting;
    private static string weTileIdText = "0";
    private static string weReplaceFromText = "0";
    private static string weReplaceToText = "1";
    private static readonly List<(int X, int Y, ushort Type, bool HasTile, ushort Wall)> weUndo = new();
    private static int weUndoMinX, weUndoMinY, weUndoMaxX, weUndoMaxY;
    // Vista de minimapa para selección WorldEdit.
    private static float weMapZoom = 1f;
    private static float weMapCenterX = -1f;
    private static float weMapCenterY = -1f;
    private static bool weMapPickPos2;

    private static string Tr(string es, string en) => spanish ? es : en;

    private static string TabName(Tab tab) => tab switch
    {
        Tab.Jugador => Tr("Jugador", "Player"),
        Tab.Aventura => Tr("Aventura", "Adventure"),
        Tab.WorldEdit => "WorldEdit",
        Tab.Mundo => Tr("Mundo", "World"),
        Tab.Editor => Tr("Editor", "Editor"),
        Tab.Buffs => Tr("Buffs", "Buffs"),
        Tab.Jefes => Tr("Jefes", "Bosses"),
        Tab.NPCs => Tr("NPC", "NPCs"),
        Tab.Proyectiles => Tr("Proyectiles", "Projectiles"),
        Tab.Teleport => Tr("Teleport", "Teleport"),
        Tab.Jugadores => Tr("Jugadores", "Players"),
        Tab.Permisos => Tr("Permisos", "Permissions"),
        Tab.Configuracion => Tr("Ajustes UI", "UI Settings"),
        _ => tab.ToString()
    };

    private static string statusMessage = "HERO's Admin listo.";
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

        if (godmodeEnabled && local.active && !IsServer)
        {
            // Usa el indicador creativo de Terraria y además mantiene vida/maná llenos.
            // La autoridad del servidor sigue mandando en multijugador.
            local.creativeGodMode = true;
            local.statLife = Math.Max(1, local.statLifeMax2);
            local.statMana = Math.Max(0, local.statManaMax2);
            for (int i = local.buffType.Length - 1; i >= 0; i--)
            {
                int buff = local.buffType[i];
                if (buff > 0 && buff < Main.debuff.Length && Main.debuff[buff])
                    local.DelBuff(i);
            }
        }
        else if (local.active && !godmodeEnabled)
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
        // La selección se gestiona desde el minimapa WorldEdit.
        

        if (panelRect.Contains(mouse))
            Main.LocalPlayer.mouseInterface = true;

        var titleRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, 30);
        if (dragging)
        {
            if (Main.mouseLeft)
            {
                position = new Vector2(mouse.X, mouse.Y) - dragOffset;
                position.X = Math.Clamp(position.X, 0f, Math.Max(0f, Main.screenWidth - Width));
                position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, Main.screenHeight - Height));
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

        // Capas visuales: sombra, borde de acento, panel y cabecera.
        sb.Draw(pixel, new Rectangle(panelRect.X + 4, panelRect.Y + 5, panelRect.Width, panelRect.Height), Color.Black * 0.42f);
        sb.Draw(pixel, new Rectangle(panelRect.X - 1, panelRect.Y - 1, panelRect.Width + 2, panelRect.Height + 2), AccentColor * 0.92f);
        sb.Draw(pixel, panelRect, new Color(23, 26, 34) * 0.985f);
        sb.Draw(pixel, titleRect, new Color((int)(AccentColor.R * 0.50f), (int)(AccentColor.G * 0.50f), (int)(AccentColor.B * 0.50f)));
        sb.Draw(pixel, new Rectangle(titleRect.X, titleRect.Bottom - 2, titleRect.Width, 2), AccentColor);
        Utils.DrawBorderString(sb, "HERO's Admin", new Vector2(panelRect.X + Pad, panelRect.Y + 6), Color.White, 0.88f);
        Utils.DrawBorderString(sb, "UNIFIED  •  ADMIN / CHEAT TOOLS", new Vector2(panelRect.X + Pad, panelRect.Y + 21), new Color(220, 225, 235), 0.48f);

        string modeText = toolMode switch
        {
            ToolMode.Normal => "MODO: NORMAL",
            ToolMode.Debug => "MODO: DEBUG",
            ToolMode.Cheat => "MODO: CHEAT",
            _ => "MODO: NORMAL"
        };
        Color modeColor = toolMode switch
        {
            ToolMode.Normal => new Color(55, 90, 65),
            ToolMode.Debug => new Color(45, 95, 150),
            ToolMode.Cheat => new Color(150, 75, 55),
            _ => new Color(55, 90, 65)
        };
        if (DrawButton(sb, pixel, new Rectangle(panelRect.X + 100, panelRect.Y + 5, 255, 20), modeText + "  ↻", mouse, modeColor))
        {
            ToggleToolMode();
        }

        if (DrawButton(sb, pixel, new Rectangle(panelRect.X + 365, panelRect.Y + 5, 48, 20), spanish ? "ES" : "EN", mouse, new Color(75, 85, 110)))
        {
            spanish = !spanish;
            SetStatus(spanish ? "Idioma: Español" : "Language: English");
        }

        var closeRect = new Rectangle(panelRect.Right - 28, panelRect.Y + 5, 20, 20);
        if (DrawButton(sb, pixel, closeRect, "X", mouse, new Color(180, 50, 50)))
        {
            Visible = false;
            return;
        }

        Tab[] tabs = [Tab.Jugador, Tab.Aventura, Tab.WorldEdit, Tab.Mundo, Tab.Editor, Tab.Buffs, Tab.Jefes, Tab.NPCs, Tab.Proyectiles, Tab.Teleport, Tab.Jugadores, Tab.Permisos, Tab.Configuracion];
        const int tabColumns = 6;
        const int tabGap = 4;
        const int tabHeight = 22;
        int tabWidth = (Width - (Pad * 2) - ((tabColumns - 1) * tabGap)) / tabColumns;
        for (int i = 0; i < tabs.Length; i++)
        {
            int row = i / tabColumns;
            int col = i % tabColumns;
            var tabRect = new Rectangle(panelRect.X + Pad + col * (tabWidth + tabGap), panelRect.Y + 36 + row * (tabHeight + 3), tabWidth, tabHeight);
            bool selected = currentTab == tabs[i];
            Color tabColor = selected ? AccentColor : new Color(45, 50, 60);
            if (DrawButton(sb, pixel, tabRect, TabName(tabs[i]), mouse, tabColor) && !selected)
            {
                currentTab = tabs[i];
                activeField = 0;
            }
        }

        int contentY = panelRect.Y + 116;
        switch (currentTab)
        {
            case Tab.Jugador:
                DrawPlayerTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Aventura:
                DrawAdventureTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.WorldEdit:
                DrawWorldEditTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Mundo:
                DrawWorldTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Editor:
                DrawItemEditorTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Buffs:
                DrawBuffEditorTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Jefes:
                DrawBossesTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.NPCs:
                DrawNPCsTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Proyectiles:
                DrawProjectilesTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Teleport:
                DrawTeleportTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Jugadores:
                DrawPlayersTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Permisos:
                DrawPermissionsTab(sb, pixel, panelRect, ref contentY);
                break;
            case Tab.Configuracion:
                DrawSettingsTab(sb, pixel, panelRect, ref contentY, mouse);
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

    // ------------------------------------------------------------------ Modo Aventura (perfil individual del cliente)

    private static void DrawAdventureTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        // Barra de sub-vistas integrada en Aventura. Items y WorldEdit no son pestañas superiores.
        int gap = 6;
        int subW = (panel.Width - Pad * 2 - gap) / 2;
        string[] labels = { Tr("Perfil", "Profile"), Tr("Objetos", "Items") };
        AdventureView[] views = { AdventureView.Perfil, AdventureView.Items };
        for (int i = 0; i < views.Length; i++)
        {
            var r = new Rectangle(panel.X + Pad + i * (subW + gap), y, subW, 24);
            Color bg = adventureView == views[i] ? AccentColor : new Color(45, 50, 60);
            if (DrawButton(sb, pixel, r, labels[i], mouse, bg))
            {
                adventureView = views[i];
                activeField = 0;
            }
        }
        y += 30;
        switch (adventureView)
        {
            case AdventureView.Perfil:
                DrawAdventureProfile(sb, pixel, panel, ref y, mouse);
                break;
            case AdventureView.Items:
                DrawItemsTab(sb, pixel, panel, ref y, mouse);
                break;
        }
    }

    private static void DrawAdventureProfile(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Modo Aventura (solo tu jugador)", "Adventure Mode (your player only)"));
        Utils.DrawBorderString(sb,
            Tr("Perfil personalizado: no cambia el mundo ni a otros jugadores.", "Custom profile: does not change the world or other players."),
            new Vector2(panel.X + Pad, y), Color.LightGray, 0.68f);
        y += 24;

        int btnW = (Width - (Pad * 2) - 8) / 2;
        string godText = godmodeEnabled ? Tr("Protección local: ACTIVADA", "Local protection: ON") : Tr("Protección local: desactivada", "Local protection: OFF");
        Color godColor = godmodeEnabled ? new Color(40, 140, 60) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), godText, mouse, godColor))
        {
            if (Main.netMode == 1)
            {
                SetStatus(Tr("El servidor remoto controla la protección; no se envió ningún comando global.", "The remote server controls protection; no global command was sent."));
            }
            else
            {
                godmodeEnabled = !godmodeEnabled;
                Main.LocalPlayer.creativeGodMode = godmodeEnabled;
                SetStatus(godmodeEnabled ? Tr("Protección local activada.", "Local protection enabled.") : Tr("Protección local desactivada.", "Local protection disabled."));
            }
        }

        string flyText = flyEnabled ? Tr("Vuelo local: ACTIVADO", "Local flight: ON") : Tr("Vuelo local: desactivado", "Local flight: OFF");
        Color flyColor = flyEnabled ? new Color(40, 100, 160) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), flyText, mouse, flyColor))
        {
            if (Main.netMode == 1)
            {
                SetStatus(Tr("El vuelo requiere soporte del servidor; no afecta a otros jugadores.", "Flight requires server support; it does not affect other players."));
            }
            else
            {
                flyEnabled = !flyEnabled;
                SetStatus(flyEnabled ? Tr("Vuelo local: WASD/flechas, Espacio para subir y Shift para bajar.", "Local flight: WASD/arrows, Space up, Shift down.") : Tr("Vuelo local desactivado.", "Local flight disabled."));
            }
        }
        y += RowH + 10;

        DrawSectionHeader(sb, panel, ref y, Tr("Compatibilidad", "Compatibility"));
        string[] notes =
        {
            Tr("• Un jugador: los controles locales pueden aplicarse al personaje.", "• Single-player: local controls can apply to your character."),
            Tr("• Servidor vanilla: el servidor decide qué cambios acepta.", "• Vanilla server: the server decides which changes it accepts."),
            Tr("• TShock: las acciones remotas requieren permisos y comandos disponibles.", "• TShock: remote actions require permissions and supported commands."),
            Tr("• No activa Journey Mode real ni cambia la dificultad global.", "• Does not enable real Journey Mode or change global difficulty.")
        };
        foreach (string note in notes)
        {
            Utils.DrawBorderString(sb, note, new Vector2(panel.X + Pad, y), Color.White, 0.67f);
            y += 21;
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
                // No se restaura vida ni mana al activar God Mode.
                // creativeGodMode es el indicador local del juego; en servidores remotos
                // no garantiza inmunidad real porque la autoridad la conserva el servidor.
                SetStatus(godmodeEnabled
                    ? Tr("God Mode local activado (no rellena vida).", "Local God Mode enabled (does not refill life).")
                    : Tr("God Mode desactivado.", "God Mode disabled."));
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

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Curar Full Vida/Maná", mouse))
        {
            if (IsServer)
            {
                SendCmd($"/heal {Quote(Main.LocalPlayer.name)}");
            }
            else
            {
                var p = Main.LocalPlayer;
                p.statLife = p.statLifeMax2;
                p.statMana = p.statManaMax2;
                SyncSelf();
                SetStatus("¡Salud y maná restaurados al máximo!");
            }
        }

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), "Limpiar Debuffs", mouse))
        {
            if (IsServer)
            {
                SetStatus("TShock no incluye un comando estándar para quitar debuffs. No se envió un comando inexistente.");
            }
            else
            {
                var p = Main.LocalPlayer;
                for (int i = p.buffType.Length - 1; i >= 0; i--)
                {
                    int buffId = p.buffType[i];
                    if (buffId > 0 && buffId < Main.debuff.Length && Main.debuff[buffId])
                        p.DelBuff(i);
                }
                SyncSelf();
                SetStatus("Efectos negativos eliminados.");
            }
        }
        y += RowH + 6;

        DrawSectionHeader(sb, panel, ref y, Tr("Editor de vida y maná", "Life and mana editor"));
        int statColW = (Width - Pad * 2 - 8) / 2;
        int statLeft = panel.X + Pad;
        int statRight = statLeft + statColW + 8;
        Utils.DrawBorderString(sb, Tr("Vida actual", "Current life"), new Vector2(statLeft, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, Tr("Vida máxima", "Maximum life"), new Vector2(statRight, y), Color.White, 0.68f);
        y += 17;
        DrawTextField(sb, pixel, new Rectangle(statLeft, y, statColW, 23), 16, playerLifeText, mouse, "100");
        DrawTextField(sb, pixel, new Rectangle(statRight, y, statColW, 23), 17, playerMaxLifeText, mouse, "100");
        y += 27;
        Utils.DrawBorderString(sb, Tr("Maná actual", "Current mana"), new Vector2(statLeft, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, Tr("Maná máximo", "Maximum mana"), new Vector2(statRight, y), Color.White, 0.68f);
        y += 17;
        DrawTextField(sb, pixel, new Rectangle(statLeft, y, statColW, 23), 18, playerManaText, mouse, "20");
        DrawTextField(sb, pixel, new Rectangle(statRight, y, statColW, 23), 19, playerMaxManaText, mouse, "20");
        y += 27;
        if (DrawButton(sb, pixel, new Rectangle(statLeft, y, statColW, RowH), Tr("Aplicar vida/maná", "Apply life/mana"), mouse, new Color(35, 120, 75)))
            ApplyPlayerStats();
        if (DrawButton(sb, pixel, new Rectangle(statRight, y, statColW, RowH), Tr("Rellenar al máximo", "Fill to maximum"), mouse, new Color(40, 100, 140)))
        {
            Player p = Main.LocalPlayer;
            p.statLife = p.statLifeMax2;
            p.statMana = p.statManaMax2;
            playerLifeText = p.statLife.ToString();
            playerMaxLifeText = p.statLifeMax.ToString();
            playerManaText = p.statMana.ToString();
            playerMaxManaText = p.statManaMax.ToString();
            SyncSelf();
            SetStatus(Tr("Vida y maná rellenados.", "Life and mana restored."));
        }
        y += RowH + 8;

        string spawnText = noSpawnsEnabled ? "Spawn Enemigos: [OFF]" : "Spawn Enemigos: [ON]";
        Color spawnColor = noSpawnsEnabled ? new Color(140, 40, 40) : new Color(50, 55, 70);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), spawnText, mouse, spawnColor))
        {
            if (InMultiplayer)
            {
                SetStatus("El spawn de enemigos lo controla el servidor Unified.");
            }
            else
            {
                noSpawnsEnabled = !noSpawnsEnabled;
                NPC.defaultSpawnRate = noSpawnsEnabled ? 999999 : 600;
                NPC.defaultMaxSpawns = noSpawnsEnabled ? 0 : 5;
                SetStatus(noSpawnsEnabled ? "Generación de monstruos bloqueada." : "Generación de monstruos normal.");
            }
        }
        y += RowH + 14;

        DrawSectionHeader(sb, panel, ref y, "Buffs Rápidos (10 minutos)");
        (int id, string name)[] buffs =
        {
            (BuffID.Regeneration, "Regeneración"),
            (BuffID.Swiftness, "Velocidad"),
            (BuffID.Ironskin, "Piel de Hierro"),
            (BuffID.ManaRegeneration, "Maná Regen"),
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

    private static void ApplyPlayerStats()
    {
        if (IsServer)
        {
            SetStatus("TShock no tiene un comando estándar /stats para editar vida y maná máximos. Usa Curar Full Vida/Maná.");
            return;
        }

        if (!int.TryParse(playerMaxLifeText, out int maxLife) || maxLife < 1 || maxLife > 1000000 ||
            !int.TryParse(playerLifeText, out int life) || life < 0 || life > 1000000 ||
            !int.TryParse(playerMaxManaText, out int maxMana) || maxMana < 0 || maxMana > 1000000 ||
            !int.TryParse(playerManaText, out int mana) || mana < 0 || mana > 1000000)
        {
            SetStatus(Tr("Valores inválidos. Usa números entre 0 y 1,000,000.", "Invalid values. Use numbers between 0 and 1,000,000."));
            return;
        }

        Player p = Main.LocalPlayer;
        p.statLifeMax = maxLife;
        p.statLife = Math.Clamp(life, 0, maxLife);
        p.statManaMax = maxMana;
        p.statMana = Math.Clamp(mana, 0, maxMana);
        playerLifeText = p.statLife.ToString();
        playerMaxLifeText = p.statLifeMax.ToString();
        playerManaText = p.statMana.ToString();
        playerMaxManaText = p.statManaMax.ToString();
        SyncSelf();
        SetStatus(Tr("Vida máxima, vida actual y maná actualizados localmente.", "Life and mana updated locally."));
    }

    // ------------------------------------------------------------------ Mundo

    private static void DrawWorldEditTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "WorldEdit - minimapa y selección");
        if (Main.netMode != 0)
        {
            Utils.DrawBorderString(sb, "Vista de selección disponible; edición directa solo en un jugador.", new Vector2(panel.X + Pad, y), Color.Orange, 0.67f);
            y += 23;
        }

        int mapW = panel.Width - Pad * 2;
        int mapH = Math.Min(190, Math.Max(130, panel.Bottom - y - 260));
        var mapRect = new Rectangle(panel.X + Pad, y, mapW, mapH);
        sb.Draw(pixel, mapRect, new Color(12, 16, 25));
        EnsureWorldMapTexture(sb);

        int btnW = (mapW - 16) / 3;
        var zoomOutRect = new Rectangle(panel.X + Pad, y + 4, btnW, 22);
        var zoomInRect = new Rectangle(zoomOutRect.Right + 8, y + 4, btnW, 22);
        var centerRect = new Rectangle(zoomInRect.Right + 8, y + 4, mapW - btnW * 2 - 16, 22);
        if (DrawButton(sb, pixel, zoomOutRect, "Zoom -", mouse))
            weMapZoom = Math.Max(1f, weMapZoom / 1.5f);
        if (DrawButton(sb, pixel, zoomInRect, "Zoom +", mouse))
            weMapZoom = Math.Min(12f, weMapZoom * 1.5f);
        if (DrawButton(sb, pixel, centerRect, "Centrar", mouse))
        {
            if (weHasPos1 && weHasPos2)
            {
                weMapCenterX = (wePos1.X + wePos2.X) * 0.5f;
                weMapCenterY = (wePos1.Y + wePos2.Y) * 0.5f;
            }
            else
            {
                weMapCenterX = Main.LocalPlayer.Center.X / 16f;
                weMapCenterY = Main.LocalPlayer.Center.Y / 16f;
            }
        }

        if (weMapCenterX < 0f || weMapCenterY < 0f)
        {
            weMapCenterX = Main.LocalPlayer.Center.X / 16f;
            weMapCenterY = Main.LocalPlayer.Center.Y / 16f;
        }

        Rectangle drawRect = Rectangle.Empty;
        Rectangle srcRect = Rectangle.Empty;
        if (worldMapTexture != null && !worldMapTexture.IsDisposed)
        {
            int innerTop = mapRect.Y + 30;
            var mapInner = new Rectangle(mapRect.X + 4, innerTop, mapRect.Width - 8, mapRect.Bottom - innerTop - 4);
            float srcW = MapTextureWidth / weMapZoom;
            float srcH = MapTextureHeight / weMapZoom;
            float centerPx = weMapCenterX / Math.Max(1f, Main.maxTilesX) * MapTextureWidth;
            float centerPy = weMapCenterY / Math.Max(1f, Main.maxTilesY) * MapTextureHeight;
            int srcX = (int)Math.Clamp(centerPx - srcW / 2f, 0f, MapTextureWidth - srcW);
            int srcY = (int)Math.Clamp(centerPy - srcH / 2f, 0f, MapTextureHeight - srcH);
            int sourceWidth = Math.Max(1, (int)srcW);
            int sourceHeight = Math.Max(1, (int)srcH);
            srcX = Math.Clamp(srcX, 0, Math.Max(0, MapTextureWidth - sourceWidth));
            srcY = Math.Clamp(srcY, 0, Math.Max(0, MapTextureHeight - sourceHeight));
            srcRect = new Rectangle(srcX, srcY, sourceWidth, sourceHeight);
            float fit = Math.Min((float)mapInner.Width / sourceWidth, (float)mapInner.Height / sourceHeight);
            int drawW = Math.Max(1, (int)(sourceWidth * fit));
            int drawH = Math.Max(1, (int)(sourceHeight * fit));
            drawRect = new Rectangle(mapInner.X + (mapInner.Width - drawW) / 2, mapInner.Y + (mapInner.Height - drawH) / 2, drawW, drawH);
            sb.Draw(worldMapTexture, drawRect, srcRect, Color.White);

            // Dibujar la selección dentro del minimapa.
            if (weHasPos1 && weHasPos2)
            {
                float tx1 = wePos1.X / (float)Math.Max(1, Main.maxTilesX) * MapTextureWidth;
                float ty1 = wePos1.Y / (float)Math.Max(1, Main.maxTilesY) * MapTextureHeight;
                float tx2 = (wePos2.X + 1) / (float)Math.Max(1, Main.maxTilesX) * MapTextureWidth;
                float ty2 = (wePos2.Y + 1) / (float)Math.Max(1, Main.maxTilesY) * MapTextureHeight;
                int sx1 = drawRect.X + (int)((tx1 - srcRect.X) / srcRect.Width * drawRect.Width);
                int sy1 = drawRect.Y + (int)((ty1 - srcRect.Y) / srcRect.Height * drawRect.Height);
                int sx2 = drawRect.X + (int)((tx2 - srcRect.X) / srcRect.Width * drawRect.Width);
                int sy2 = drawRect.Y + (int)((ty2 - srcRect.Y) / srcRect.Height * drawRect.Height);
                var selection = Rectangle.Intersect(drawRect, new Rectangle(Math.Min(sx1, sx2), Math.Min(sy1, sy2), Math.Max(2, Math.Abs(sx2 - sx1)), Math.Max(2, Math.Abs(sy2 - sy1))));
                if (selection.Width > 0 && selection.Height > 0)
                {
                    sb.Draw(pixel, new Rectangle(selection.X, selection.Y, selection.Width, 2), Color.LimeGreen);
                    sb.Draw(pixel, new Rectangle(selection.X, selection.Bottom - 2, selection.Width, 2), Color.LimeGreen);
                    sb.Draw(pixel, new Rectangle(selection.X, selection.Y, 2, selection.Height), Color.LimeGreen);
                    sb.Draw(pixel, new Rectangle(selection.Right - 2, selection.Y, 2, selection.Height), Color.LimeGreen);
                }
            }

            // Posición del jugador.
            float playerPx = Main.LocalPlayer.Center.X / 16f / Math.Max(1f, Main.maxTilesX) * MapTextureWidth;
            float playerPy = Main.LocalPlayer.Center.Y / 16f / Math.Max(1f, Main.maxTilesY) * MapTextureHeight;
            int playerX = drawRect.X + (int)((playerPx - srcRect.X) / srcRect.Width * drawRect.Width);
            int playerY = drawRect.Y + (int)((playerPy - srcRect.Y) / srcRect.Height * drawRect.Height);
            if (drawRect.Contains(playerX, playerY))
                sb.Draw(pixel, new Rectangle(playerX - 3, playerY - 3, 7, 7), Color.Yellow);

            if (Clicked && drawRect.Contains(mouse))
            {
                Main.mouseLeftRelease = false;
                float mapPx = srcRect.X + (mouse.X - drawRect.X) / (float)drawRect.Width * srcRect.Width;
                float mapPy = srcRect.Y + (mouse.Y - drawRect.Y) / (float)drawRect.Height * srcRect.Height;
                int tileX = Math.Clamp((int)(mapPx / MapTextureWidth * Main.maxTilesX), 1, Main.maxTilesX - 2);
                int tileY = Math.Clamp((int)(mapPy / MapTextureHeight * Main.maxTilesY), 1, Main.maxTilesY - 2);
                var pos = new Point(tileX, tileY);
                if (weMapPickPos2 || weHasPos1)
                {
                    wePos2 = pos;
                    weHasPos2 = IsWorldEditPointValid(pos);
                    weMapPickPos2 = false;
                    SetStatus($"WorldEdit Pos2: {tileX}, {tileY}");
                }
                else
                {
                    wePos1 = pos;
                    weHasPos1 = IsWorldEditPointValid(pos);
                    weMapPickPos2 = true;
                    SetStatus($"WorldEdit Pos1: {tileX}, {tileY}. Haz clic otra vez para Pos2.");
                }
                weMapCenterX = tileX;
                weMapCenterY = tileY;
            }
        }
        else
        {
            Utils.DrawBorderString(sb, "Mapa no disponible; pulsa Recargar mapa en Teleport.", new Vector2(mapRect.X + 8, mapRect.Y + 8), Color.Orange, 0.65f);
        }
        y += mapH + 5;

        string p1 = weHasPos1 ? $"Pos1: {wePos1.X}, {wePos1.Y}" : "Pos1: sin seleccionar";
        string p2 = weHasPos2 ? $"Pos2: {wePos2.X}, {wePos2.Y}" : "Pos2: sin seleccionar";
        Utils.DrawBorderString(sb, p1, new Vector2(panel.X + Pad, y), Color.LightGreen, 0.68f);
        Utils.DrawBorderString(sb, p2, new Vector2(panel.X + Pad + mapW / 2, y), Color.LightSkyBlue, 0.68f);
        y += 22;

        int half = (mapW - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, half, RowH), "Marcar Pos1", mouse))
        {
            wePos1 = new Point((int)(Main.MouseWorld.X / 16f), (int)(Main.MouseWorld.Y / 16f));
            weHasPos1 = IsWorldEditPointValid(wePos1);
            if (weHasPos1) { weMapCenterX = wePos1.X; weMapCenterY = wePos1.Y; }
            SetStatus(weHasPos1 ? $"Pos1: {wePos1.X}, {wePos1.Y}" : "Pos1 fuera del mundo.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + half + 8, y, half, RowH), "Marcar Pos2", mouse))
        {
            wePos2 = new Point((int)(Main.MouseWorld.X / 16f), (int)(Main.MouseWorld.Y / 16f));
            weHasPos2 = IsWorldEditPointValid(wePos2);
            if (weHasPos2) { weMapCenterX = wePos2.X; weMapCenterY = wePos2.Y; }
            SetStatus(weHasPos2 ? $"Pos2: {wePos2.X}, {wePos2.Y}" : "Pos2 fuera del mundo.");
        }
        y += RowH + 5;

        int fw = (mapW - 12) / 3;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, fw, RowH), 8101, weTileIdText, mouse, "Tile ID");
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + fw + 6, y, fw, RowH), "Rellenar", mouse, new Color(45, 115, 70)))
        {
            if (Main.netMode == 0) WorldEditFill();
            else SetStatus("Fill directo solo está disponible en un jugador; no se enviaron comandos.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + (fw + 6) * 2, y, fw, RowH), "Limpiar", mouse, new Color(125, 65, 55)))
        {
            if (Main.netMode == 0) WorldEditClear();
            else SetStatus("Clear directo solo está disponible en un jugador; no se enviaron comandos.");
        }
        y += RowH + 5;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, fw, RowH), 8102, weReplaceFromText, mouse, "ID origen");
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + fw + 6, y, fw, RowH), 8103, weReplaceToText, mouse, "ID destino");
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + (fw + 6) * 2, y, fw, RowH), "Reemplazar", mouse, new Color(85, 85, 145)))
        {
            if (Main.netMode == 0) WorldEditReplace();
            else SetStatus("Replace directo solo está disponible en un jugador; no se enviaron comandos.");
        }
        y += RowH + 5;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, half, RowH), "Deshacer", mouse, new Color(100, 85, 55)))
        {
            if (Main.netMode == 0) WorldEditUndo();
            else SetStatus("Undo directo solo está disponible en un jugador; no se enviaron comandos.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + half + 8, y, half, RowH), "Deseleccionar", mouse))
        {
            weHasPos1 = weHasPos2 = false;
            weMapPickPos2 = false;
            SetStatus("Selección WorldEdit borrada.");
        }
        y += RowH + 4;
        Utils.DrawBorderString(sb, "Clic en el mapa: Pos1 y luego Pos2. Verde = selección; amarillo = jugador.", new Vector2(panel.X + Pad, y), Color.LightGray, 0.60f);
        y += 18;
    }
    private static string FilterDigits(string input, int maxLength)
    {
        var chars = new System.Text.StringBuilder(input.Length);
        foreach (char c in input) if (char.IsDigit(c) && chars.Length < maxLength) chars.Append(c);
        return chars.ToString();
    }

    private static bool IsWorldEditPointValid(Point p) => p.X >= 1 && p.Y >= 1 && p.X < Main.maxTilesX - 1 && p.Y < Main.maxTilesY - 1;

    private static bool TryWorldEditSelection(out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = maxX = maxY = 0;
        if (!weHasPos1 || !weHasPos2) { SetStatus("Marca Pos1 y Pos2 primero."); return false; }
        minX = Math.Min(wePos1.X, wePos2.X); minY = Math.Min(wePos1.Y, wePos2.Y);
        maxX = Math.Max(wePos1.X, wePos2.X); maxY = Math.Max(wePos1.Y, wePos2.Y);
        long area = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (area > 10000) { SetStatus($"Selección demasiado grande ({area} bloques); máximo 10000."); return false; }
        return true;
    }

    private static void WorldEditSaveUndo(int minX, int minY, int maxX, int maxY)
    {
        weUndo.Clear(); weUndoMinX = minX; weUndoMinY = minY; weUndoMaxX = maxX; weUndoMaxY = maxY;
        for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
        {
            var t = Main.tile[x, y];
            weUndo.Add((x, y, t.TileType, t.HasTile, t.WallType));
        }
    }

    private static void WorldEditFill()
    {
        if (!TryWorldEditSelection(out int x1, out int y1, out int x2, out int y2)) return;
        if (!ushort.TryParse(weTileIdText, out ushort id) || id >= TileID.Count) { SetStatus("ID de bloque inválido."); return; }
        if (Main.netMode == 1) { SendHerosCmd($"worldedit fill {x1} {y1} {x2} {y2} {id}"); return; }
        WorldEditSaveUndo(x1, y1, x2, y2);
        for (int x = x1; x <= x2; x++)
        for (int y = y1; y <= y2; y++)
        {
            if (Main.tile[x, y].HasTile)
                WorldGen.KillTile(x, y, false, false, true);
            WorldGen.PlaceTile(x, y, id, true, true);
        }
        WorldEditFrame(x1, y1, x2, y2); SetStatus("Selección rellenada.");
    }

    private static void WorldEditClear()
    {
        if (!TryWorldEditSelection(out int x1, out int y1, out int x2, out int y2)) return;
        if (Main.netMode == 1) { SendHerosCmd($"worldedit clear {x1} {y1} {x2} {y2}"); return; }
        WorldEditSaveUndo(x1, y1, x2, y2);
        for (int x = x1; x <= x2; x++)
        for (int y = y1; y <= y2; y++)
        {
            WorldGen.KillTile(x, y, false, false, true);
        }
        WorldEditFrame(x1, y1, x2, y2); SetStatus("Bloques eliminados; paredes conservadas.");
    }

    private static void WorldEditReplace()
    {
        if (!TryWorldEditSelection(out int x1, out int y1, out int x2, out int y2)) return;
        if (!ushort.TryParse(weReplaceFromText, out ushort from) || from >= TileID.Count || !ushort.TryParse(weReplaceToText, out ushort to) || to >= TileID.Count) { SetStatus("ID origen/destino inválido."); return; }
        if (Main.netMode == 1) { SendHerosCmd($"worldedit replace {x1} {y1} {x2} {y2} {from} {to}"); return; }
        WorldEditSaveUndo(x1, y1, x2, y2); int changed = 0;
        for (int x = x1; x <= x2; x++)
        for (int y = y1; y <= y2; y++)
        {
            if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == from)
            {
                WorldGen.KillTile(x, y, false, false, true);
                WorldGen.PlaceTile(x, y, to, true, true);
                changed++;
            }
        }
        WorldEditFrame(x1, y1, x2, y2); SetStatus($"Bloques reemplazados: {changed}.");
    }

    private static void WorldEditUndo()
    {
        if (Main.netMode == 1) { SendHerosCmd("worldedit undo"); return; }
        if (weUndo.Count == 0) { SetStatus("No hay una edición local para deshacer."); return; }
        foreach (var v in weUndo)
        {
            if (Main.tile[v.X, v.Y].HasTile)
                WorldGen.KillTile(v.X, v.Y, false, false, true);

            if (v.HasTile)
                WorldGen.PlaceTile(v.X, v.Y, v.Type, true, true);

            ushort currentWall = Main.tile[v.X, v.Y].WallType;
            if (currentWall != v.Wall)
            {
                if (currentWall != 0)
                    WorldGen.KillWall(v.X, v.Y);
                if (v.Wall != 0)
                    WorldGen.PlaceWall(v.X, v.Y, v.Wall, true);
            }
        }
        WorldEditFrame(weUndoMinX, weUndoMinY, weUndoMaxX, weUndoMaxY); weUndo.Clear(); SetStatus("Última edición deshecha.");
    }

    private static void WorldEditFrame(int x1, int y1, int x2, int y2)
    {
        for (int x = x1; x <= x2; x++) for (int y = y1; y <= y2; y++) WorldGen.SquareTileFrame(x, y);
        worldMapNeedsRefresh = true;
    }

    private static void DrawWorldTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Control de Tiempo y Clima");
        int btnW = (Width - (Pad * 2) - 8) / 2;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Establecer: Día", mouse))
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
                SetStatus("Hora cambiada al Día.");
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

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, btnW, RowH), "Establecer: Mediodía", mouse))
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
                SetStatus("Hora cambiada al Mediodía.");
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
                SetStatus("TShock estándar no incluye /rain. No se envió un comando inexistente.");
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
                        // Esta ruta solo modifica el estado local del cliente. No puede forzar
                        // la muerte real de NPC controlados por un servidor remoto.
                        npc.life = 0;
                        npc.active = false;
                        count++;
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
        DrawSectionHeader(sb, panel, ref y, "Generador de Objetos (Spawn de Ítems)");

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
        y += RowH + 6;

        Utils.DrawBorderString(sb, Tr("Daño (0=normal):", "Damage (0=default):"), new Vector2(panel.X + Pad, y), Color.White, 0.7f);
        Utils.DrawBorderString(sb, Tr("Defensa (0=normal):", "Defense (0=default):"), new Vector2(panel.X + Pad + 190, y), Color.White, 0.7f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 150, RowH), 6, itemDamageText, mouse, "0");
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + 190, y, 150, RowH), 7, itemDefenseText, mouse, "0");
        y += RowH + 8;

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

    // ------------------------------------------------------------------ Editor de inventario

    private static void DrawItemEditorTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Editor avanzado de objetos", "Advanced item editor"));
        Utils.DrawBorderString(sb, Tr("Buscar objeto para crearlo en el slot seleccionado:", "Search item to create in selected slot:"), new Vector2(panel.X + Pad, y), Color.White, 0.68f);
        y += 18;
        int searchButtonW = 145;
        int pageButtonW = 100;
        int searchFieldW = Width - Pad * 2 - searchButtonW - pageButtonW - 16;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, searchFieldW, RowH), 1, itemQuery, mouse, "Nombre o ID");
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + searchFieldW + 4, y, searchButtonW, RowH), Tr("Crear en slot", "Create in slot"), mouse, new Color(40, 110, 160)))
            LoadItemIntoEditorSlot();
        string editorPageLabel = !editorAdvancedPage ? Tr("Avanzado", "Advanced") : (editorExtraPage ? Tr("Básico", "Basic") : Tr("Más opciones", "More options"));
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + searchFieldW + searchButtonW + 8, y, pageButtonW, RowH), editorPageLabel, mouse, new Color(85, 75, 125)))
        {
            if (!editorAdvancedPage) { editorAdvancedPage = true; editorExtraPage = false; }
            else if (!editorExtraPage) editorExtraPage = true;
            else { editorAdvancedPage = false; editorExtraPage = false; }
            activeField = 0;
        }
        y += RowH + 6;

        var player = Main.LocalPlayer;
        int maxSlot = Math.Max(0, player.inventory.Length - 1);
        int.TryParse(editorSlotText, out int selectedSlot);
        selectedSlot = Math.Clamp(selectedSlot, 0, maxSlot);
        var selected = player.inventory[selectedSlot];
        if (editorLoadedSlot != selectedSlot)
        {
            editorLoadedSlot = selectedSlot;
            if (selected != null && !selected.IsAir)
            {
                editorStackText = selected.stack.ToString();
                editorDamageText = selected.damage.ToString();
                editorDefenseText = selected.defense.ToString();
                editorCritText = selected.crit.ToString();
                editorRarityText = selected.rare.ToString();
                editorKnockbackText = selected.knockBack.ToString(System.Globalization.CultureInfo.InvariantCulture);
                editorPrefixText = selected.prefix.ToString();
                editorUseTimeText = selected.useTime.ToString();
                editorUseAnimationText = selected.useAnimation.ToString();
                editorUseStyleText = selected.useStyle.ToString();
                editorManaText = selected.mana.ToString();
                editorShootText = selected.shoot.ToString();
                editorShootSpeedText = selected.shootSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                editorScaleText = selected.scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                editorValueText = selected.value.ToString();
                editorPickText = selected.pick.ToString();
                editorAxeText = selected.axe.ToString();
                editorHealLifeText = selected.healLife.ToString();
                editorHealManaText = selected.healMana.ToString();
                editorAmmoText = selected.ammo.ToString();
                editorUseAmmoText = selected.useAmmo.ToString();
                editorCreateTileText = selected.createTile.ToString();
                editorCreateWallText = selected.createWall.ToString();
                editorBuffTypeText = selected.buffType.ToString();
                editorBuffTimeText = selected.buffTime.ToString();
                editorMountTypeText = selected.mountType.ToString();
                editorUseTurn = selected.useTurn;
                editorNoMelee = selected.noMelee;
                editorNoUseGraphic = selected.noUseGraphic;
                editorAutoReuse = selected.autoReuse;
                editorConsumable = selected.consumable;
            }
            else
            {
                editorStackText = "1";
                editorDamageText = "0";
                editorDefenseText = "0";
                editorCritText = "0";
                editorRarityText = "0";
                editorKnockbackText = "0";
                editorPrefixText = "0";
                editorUseTimeText = "0";
                editorUseAnimationText = "0";
                editorUseStyleText = "0";
                editorManaText = "0";
                editorShootText = "0";
                editorShootSpeedText = "0";
                editorScaleText = "1";
                editorValueText = "0";
                editorPickText = "0";
                editorAxeText = "0";
                editorHealLifeText = "0";
                editorHealManaText = "0";
                editorAmmoText = "0";
                editorUseAmmoText = "0";
                editorCreateTileText = "-1";
                editorCreateWallText = "-1";
                editorBuffTypeText = "0";
                editorBuffTimeText = "0";
                editorMountTypeText = "-1";
                editorUseTurn = false;
                editorNoMelee = false;
                editorNoUseGraphic = false;
                editorAutoReuse = false;
                editorConsumable = false;
            }
        }

        string itemName = selected == null || selected.IsAir ? Tr("(vacío)", "(empty)") : selected.Name;
        Utils.DrawBorderString(sb, $"{Tr("Jugador", "Player")}: {player.name}   |   Slot: {selectedSlot}/{maxSlot}", new Vector2(panel.X + Pad, y), Color.LightGray, 0.72f);
        y += 20;
        Utils.DrawBorderString(sb, $"{Tr("Objeto", "Item")}: {itemName}", new Vector2(panel.X + Pad, y), Color.Gold, 0.78f);
        y += 22;

        int colW = (Width - Pad * 2 - 12) / 2;
        int left = panel.X + Pad;
        int right = left + colW + 12;


        if (!editorAdvancedPage)
        {
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Slot", "Slot"), Tr("Cantidad", "Stack"), 8, editorSlotText, 9, editorStackText, "0", "1");
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Daño", "Damage"), Tr("Defensa", "Defense"), 10, editorDamageText, 11, editorDefenseText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Crítico", "Crit"), Tr("Rareza (ID)", "Rarity (ID)"), 12, editorCritText, 13, editorRarityText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Retroceso", "Knockback"), Tr("Prefijo (ID)", "Prefix (ID)"), 14, editorKnockbackText, 15, editorPrefixText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Escala", "Scale"), Tr("Valor en monedas", "Value in coins"), 26, editorScaleText, 27, editorValueText, "1", "0");
        }
        else if (!editorExtraPage)
        {
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Tiempo de uso", "Use time"), Tr("Animación de uso", "Use animation"), 20, editorUseTimeText, 21, editorUseAnimationText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Estilo de uso (ID)", "Use style (ID)"), Tr("Maná requerido", "Mana cost"), 22, editorUseStyleText, 23, editorManaText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Proyectil (ID)", "Projectile (ID)"), Tr("Velocidad del proyectil", "Projectile speed"), 24, editorShootText, 25, editorShootSpeedText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Poder de pico", "Pickaxe power"), Tr("Poder de hacha", "Axe power"), 30, editorPickText, 31, editorAxeText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Curación de vida", "Life healing"), Tr("Curación de maná", "Mana healing"), 32, editorHealLifeText, 33, editorHealManaText);
        }
        else
        {
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Munición (ID)", "Ammo (ID)"), Tr("Tipo de munición (ID)", "Use ammo (ID)"), 34, editorAmmoText, 35, editorUseAmmoText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Crear bloque (ID; -1 ninguno)", "Create tile (ID; -1 none)"), Tr("Crear pared (ID; -1 ninguna)", "Create wall (ID; -1 none)"), 36, editorCreateTileText, 37, editorCreateWallText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Buff al usar (ID)", "Buff on use (ID)"), Tr("Duración buff (ticks)", "Buff duration (ticks)"), 38, editorBuffTypeText, 39, editorBuffTimeText);
            DrawEditorPair(sb, pixel, panel, ref y, mouse, left, right, colW, Tr("Montura (ID; -1 ninguna)", "Mount (ID; -1 none)"), Tr("Valor en monedas", "Value in coins"), 40, editorMountTypeText, 27, editorValueText);
        }

        int half = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(left, y, half, RowH), editorAutoReuse ? Tr("Autouso: SÍ", "Auto reuse: ON") : Tr("Autouso: NO", "Auto reuse: OFF"), mouse, editorAutoReuse ? new Color(35, 120, 75) : new Color(75, 75, 85)))
            editorAutoReuse = !editorAutoReuse;
        if (DrawButton(sb, pixel, new Rectangle(left + half + 8, y, half, RowH), editorConsumable ? Tr("Consumible: SÍ", "Consumable: ON") : Tr("Consumible: NO", "Consumable: OFF"), mouse, editorConsumable ? new Color(35, 120, 75) : new Color(75, 75, 85)))
            editorConsumable = !editorConsumable;
        y += RowH + 4;
        if (DrawButton(sb, pixel, new Rectangle(left, y, colW, RowH), editorUseTurn ? Tr("Giro al usar: SÍ", "Use turn: ON") : Tr("Giro al usar: NO", "Use turn: OFF"), mouse)) editorUseTurn = !editorUseTurn;
        if (DrawButton(sb, pixel, new Rectangle(right, y, colW, RowH), editorNoMelee ? Tr("Sin daño cuerpo a cuerpo: SÍ", "No melee: ON") : Tr("Sin daño cuerpo a cuerpo: NO", "No melee: OFF"), mouse)) editorNoMelee = !editorNoMelee;
        y += RowH + 4;
        if (DrawButton(sb, pixel, new Rectangle(left, y, colW, RowH), editorNoUseGraphic ? Tr("Sin gráfico al usar: SÍ", "No use graphic: ON") : Tr("Sin gráfico al usar: NO", "No use graphic: OFF"), mouse)) editorNoUseGraphic = !editorNoUseGraphic;
        y += RowH + 6;
        if (DrawButton(sb, pixel, new Rectangle(left, y, half, RowH), Tr("Aplicar cambios", "Apply changes"), mouse, new Color(35, 120, 75)))
            ApplyInventoryItemEdits();
        if (DrawButton(sb, pixel, new Rectangle(left + half + 8, y, half, RowH), Tr("Alternar favorito", "Toggle favorite"), mouse, new Color(105, 80, 145)))
            ToggleInventoryFavorite();
        y += RowH + 6;
        if (DrawButton(sb, pixel, new Rectangle(left, y, half, RowH), Tr("Slot anterior", "Previous slot"), mouse))
            editorSlotText = Math.Max(0, selectedSlot - 1).ToString();
        if (DrawButton(sb, pixel, new Rectangle(left + half + 8, y, half, RowH), Tr("Slot siguiente", "Next slot"), mouse))
            editorSlotText = Math.Min(maxSlot, selectedSlot + 1).ToString();
        y += RowH + 6;
        Utils.DrawBorderString(sb, Tr("Editor local: no ejecuta comandos. En multijugador el servidor puede revertir cambios.", "Local editor: no commands. In multiplayer, the server may revert changes."), new Vector2(left, y), new Color(230, 190, 100), 0.56f);
    }

    private static void DrawEditorPair(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse, int left, int right, int colW, string leftLabel, string rightLabel, int leftId, string leftValue, int rightId, string rightValue, string leftHint = "0", string rightHint = "0")
    {
        Utils.DrawBorderString(sb, leftLabel, new Vector2(left, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, rightLabel, new Vector2(right, y), Color.White, 0.68f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(left, y, colW, RowH), leftId, leftValue, mouse, leftHint);
        DrawTextField(sb, pixel, new Rectangle(right, y, colW, RowH), rightId, rightValue, mouse, rightHint);
        y += RowH + 6;
    }

    private static void LoadItemIntoEditorSlot()
    {
        if (!int.TryParse(editorSlotText, out int slot) || slot < 0 || slot >= Main.LocalPlayer.inventory.Length)
        {
            SetStatus(Tr("Primero selecciona un slot válido.", "Select a valid inventory slot first."));
            return;
        }
        int id = ResolveItemId(itemQuery);
        if (id <= 0 || id >= ItemID.Count)
        {
            SetStatus(Tr("No se encontró el objeto buscado.", "Could not find the requested item."));
            return;
        }
        int count = int.TryParse(editorStackText, out int parsed) ? Math.Clamp(parsed, 1, 9999) : 1;
        int prefix = int.TryParse(editorPrefixText, out int parsedPrefix) ? Math.Clamp(parsedPrefix, 0, PrefixID.Count - 1) : 0;
        // Editor local: modifica directamente el inventario del cliente y no envía comandos.
        Item target = Main.LocalPlayer.inventory[slot];
        target.SetDefaults(id);
        target.stack = Math.Min(count, Math.Max(1, target.maxStack));
        if (prefix > 0)
            target.Prefix(prefix);
        editorLoadedSlot = -1;
        SetStatus(Tr($"Creado {Lang.GetItemNameValue(id)} en el slot {slot}.", $"Created {Lang.GetItemNameValue(id)} in slot {slot}."));
    }

    private static bool TryGetInventorySlot(out Player player, out int slot, out Item item)
    {
        player = Main.LocalPlayer;
        slot = 0;
        item = null;
        if (!int.TryParse(editorSlotText, out slot) || slot < 0 || slot >= player.inventory.Length)
        {
            SetStatus(Tr("Slot inválido.", "Invalid slot."));
            return false;
        }
        item = player.inventory[slot];
        if (item == null || item.IsAir)
        {
            SetStatus(Tr("Ese slot está vacío.", "That slot is empty."));
            return false;
        }
        return true;
    }

    private static void ApplyInventoryItemEdits()
    {
        // No se usa /itemedit ni ningún otro comando: se editan los campos del Item local.
        if (!TryGetInventorySlot(out _, out _, out Item item)) return;
        if (int.TryParse(editorStackText, out int stack)) item.stack = Math.Clamp(stack, 1, Math.Max(1, item.maxStack));
        if (int.TryParse(editorDamageText, out int damage)) item.damage = Math.Clamp(damage, 0, 1000000);
        if (int.TryParse(editorDefenseText, out int defense)) item.defense = Math.Clamp(defense, 0, 1000000);
        if (int.TryParse(editorCritText, out int crit)) item.crit = Math.Clamp(crit, -100, 1000);
        if (int.TryParse(editorRarityText, out int rarity)) item.rare = Math.Clamp(rarity, -11, 20);
        if (float.TryParse(editorKnockbackText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float knockback))
            item.knockBack = Math.Clamp(knockback, 0f, 1000f);
        if (int.TryParse(editorPrefixText, out int prefix) && prefix >= 0 && prefix < PrefixID.Count)
            item.Prefix(prefix);
        if (int.TryParse(editorUseTimeText, out int useTime)) item.useTime = Math.Clamp(useTime, 0, 100000);
        if (int.TryParse(editorUseAnimationText, out int useAnimation)) item.useAnimation = Math.Clamp(useAnimation, 0, 100000);
        if (int.TryParse(editorUseStyleText, out int useStyle)) item.useStyle = Math.Clamp(useStyle, 0, 1000);
        if (int.TryParse(editorManaText, out int mana)) item.mana = Math.Clamp(mana, 0, 100000);
        if (int.TryParse(editorShootText, out int shoot)) item.shoot = Math.Clamp(shoot, 0, ProjectileID.Count - 1);
        if (float.TryParse(editorShootSpeedText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float shootSpeed))
            item.shootSpeed = Math.Clamp(shootSpeed, 0f, 10000f);
        if (float.TryParse(editorScaleText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale))
            item.scale = Math.Clamp(scale, 0.1f, 20f);
        if (int.TryParse(editorValueText, out int value)) item.value = Math.Clamp(value, 0, int.MaxValue);
        if (int.TryParse(editorPickText, out int pick)) item.pick = Math.Clamp(pick, 0, 10000);
        if (int.TryParse(editorAxeText, out int axe)) item.axe = Math.Clamp(axe, 0, 10000);
        if (int.TryParse(editorHealLifeText, out int healLife)) item.healLife = Math.Clamp(healLife, 0, 100000);
        if (int.TryParse(editorHealManaText, out int healMana)) item.healMana = Math.Clamp(healMana, 0, 100000);
        if (int.TryParse(editorAmmoText, out int ammo)) item.ammo = Math.Clamp(ammo, 0, 10000);
        if (int.TryParse(editorUseAmmoText, out int useAmmo)) item.useAmmo = Math.Clamp(useAmmo, 0, 10000);
        if (int.TryParse(editorCreateTileText, out int createTile)) item.createTile = Math.Clamp(createTile, -1, 10000);
        if (int.TryParse(editorCreateWallText, out int createWall)) item.createWall = Math.Clamp(createWall, -1, 10000);
        if (int.TryParse(editorBuffTypeText, out int buffType)) item.buffType = Math.Clamp(buffType, 0, BuffID.Count - 1);
        if (int.TryParse(editorBuffTimeText, out int buffTime)) item.buffTime = Math.Clamp(buffTime, 0, 10000000);
        if (int.TryParse(editorMountTypeText, out int mountType)) item.mountType = Math.Clamp(mountType, -1, 10000);
        item.useTurn = editorUseTurn;
        item.noMelee = editorNoMelee;
        item.noUseGraphic = editorNoUseGraphic;
        item.autoReuse = editorAutoReuse;
        item.consumable = editorConsumable;
        editorLoadedSlot = -1;
        SetStatus(Tr("Cambios aplicados al objeto del inventario.", "Inventory item updated."));
    }

    private static void ToggleInventoryFavorite()
    {
        // Alterna favorito directamente en el objeto local; no envía comandos.
        if (!TryGetInventorySlot(out _, out _, out Item item)) return;
        item.favorited = !item.favorited;
        SetStatus(item.favorited ? Tr("Objeto marcado como favorito.", "Item favorited.") : Tr("Favorito quitado.", "Favorite removed."));
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
        DrawSectionHeader(sb, panel, ref y, Tr("Jefes (en servidor requiere autorización)", "Bosses (server authorization required)"));
        int btnW = (Width - (Pad * 2) - 8) / 2;
        (int id, string es, string en, string cmd)[] bosses =
        {
            (50, "Rey Slime", "King Slime", "king"),
            (4, "Ojo de Cthulhu", "Eye of Cthulhu", "eye"),
            (13, "Devoramundos", "Eater of Worlds", "eater"),
            (35, "Skeletron", "Skeletron", "skeletron"),
            (222, "Abeja Reina", "Queen Bee", "queenbee"),
            (113, "Muro de Carne", "Wall of Flesh", "wall"),
            (134, "Destructor", "The Destroyer", "destroyer"),
            (125, "Los Gemelos", "The Twins", "twins"),
            (127, "Esqueletrón Prime", "Skeletron Prime", "skeletronprime"),
            (262, "Plantera", "Plantera", "plantera"),
            (245, "Gólem", "Golem", "golem"),
            (370, "Duque Fishron", "Duke Fishron", "fishron"),
            (439, "Cultista Lunático", "Lunatic Cultist", "lunaticcultist"),
            (398, "Señor de la Luna", "Moon Lord", "moonlord"),
        };
        for (int i = 0; i < bosses.Length; i++)
        {
            int col = i % 2;
            var rect = new Rectangle(panel.X + Pad + col * (btnW + 8), y, btnW, RowH);
            var b = bosses[i];
            if (DrawButton(sb, pixel, rect, spanish ? b.es : b.en, mouse))
                SpawnBoss(b.id, spanish ? b.es : b.en, b.cmd);
            if (col == 1 || i == bosses.Length - 1)
                y += RowH + 5;
        }
        Utils.DrawBorderString(sb, Tr("En multijugador, el servidor puede rechazar la invocación.", "In multiplayer, the server may reject boss spawning."),
            new Vector2(panel.X + Pad, Math.Min(y + 2, panel.Bottom - 26)), new Color(220, 190, 110), 0.62f);
    }

    // ------------------------------------------------------------------ NPCs

    private static int ResolveNpcId(string query)
    {
        if (int.TryParse(query, out int parsed))
            return parsed;
        if (string.IsNullOrWhiteSpace(query))
            return 0;
        for (int i = 1; i < NPCID.Count; i++)
        {
            string name = Lang.GetNPCNameValue(i);
            if (!string.IsNullOrEmpty(name) && name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private static void DrawNPCsTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Administrador de NPC", "NPC manager"));
        Utils.DrawBorderString(sb, Tr("Nombre o ID del NPC", "NPC name or ID"), new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, Tr("Cantidad", "Amount"), new Vector2(panel.X + 365, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 340, RowH), 28, npcQuery, mouse, "Ej. Zombie o 3");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 365, y, 100, RowH), 29, npcAmountText, mouse, "1");
        y += RowH + 7;
        int half = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, half, RowH), Tr("Invocar NPC", "Spawn NPC"), mouse, new Color(40, 115, 70)))
            SpawnNPCAction();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + half + 8, y, half, RowH), Tr("Eliminar enemigos locales", "Remove local hostile NPCs"), mouse, new Color(145, 55, 45)))
            RemoveHostileNPCs();
        y += RowH + 12;
        DrawSectionHeader(sb, panel, ref y, Tr("NPC activos (hasta 9 mostrados)", "Active NPCs (up to 9 shown)"));
        int shown = 0;
        for (int i = 0; i < Main.npc.Length && shown < 9; i++)
        {
            NPC npc = Main.npc[i];
            if (npc == null || !npc.active)
                continue;
            if (y + RowH > panel.Bottom - 42)
                break;
            string name = Lang.GetNPCNameValue(npc.type);
            if (string.IsNullOrEmpty(name)) name = $"NPC {npc.type}";
            string label = $"{name}  [{npc.life}/{Math.Max(1, npc.lifeMax)}]";
            Utils.DrawBorderString(sb, label, new Vector2(panel.X + Pad, y + 5), Color.White, 0.68f);
            if (DrawButton(sb, pixel, new Rectangle(panel.Right - Pad - 92, y, 92, RowH), Tr("Eliminar", "Remove"), mouse, new Color(130, 55, 50)))
            {
                if (IsServer)
                    SetStatus(Tr("La eliminación individual de NPC requiere un comando/plugin del servidor.", "Removing one NPC requires a server command/plugin."));
                else
                {
                    npc.active = false;
                    SetStatus(Tr($"NPC {name} eliminado localmente.", $"NPC {name} removed locally."));
                }
            }
            y += RowH + 4;
            shown++;
        }
        if (shown == 0)
            Utils.DrawBorderString(sb, Tr("No hay NPC activos visibles.", "No active NPCs visible."), new Vector2(panel.X + Pad, y), Color.Gray, 0.7f);
        Utils.DrawBorderString(sb, Tr("En multijugador, el servidor decide si acepta invocaciones o eliminaciones en modo DIRECTO.", "In multiplayer, the server decides whether to accept spawns/removals in DIRECT mode."), new Vector2(panel.X + Pad, panel.Bottom - 36), new Color(230, 190, 100), 0.58f);
    }

    private static void SpawnNPCAction()
    {
        int id = ResolveNpcId(npcQuery);
        if (id <= 0 || id >= NPCID.Count)
        {
            SetStatus(Tr("ID o nombre de NPC no válido.", "Invalid NPC name or ID."));
            return;
        }
        int count = int.TryParse(npcAmountText, out int amount) ? Math.Clamp(amount, 1, 20) : 1;
        if (IsServer)
        {
            SendCmd($"/spawnmob {id} {count}");
            return;
        }
        if (InMultiplayer)
        {
            SetStatus(Tr("La invocación se enviará al servidor Unified para que la ejecute.", "The spawn request is sent to Unified server for execution."));
        }
        int spawned = 0;
        Player player = Main.LocalPlayer;
        for (int i = 0; i < count; i++)
        {
            int index = NPC.NewNPC(new PanelItemSource(), (int)player.Center.X, (int)player.Center.Y, id);
            if (index >= 0 && index < Main.npc.Length)
                spawned++;
        }
        SetStatus(Tr($"Invocación solicitada: {Lang.GetNPCNameValue(id)} x{spawned}.", $"Spawn requested: {Lang.GetNPCNameValue(id)} x{spawned}."));
    }

    private static void RemoveHostileNPCs()
    {
        if (IsServer)
        {
            SendCmd("/butcher");
            return;
        }
        int removed = 0;
        foreach (NPC npc in Main.npc)
        {
            if (npc != null && npc.active && !npc.friendly && !npc.townNPC && npc.lifeMax > 5)
            {
                npc.active = false;
                removed++;
            }
        }
        SetStatus(Tr($"Eliminados localmente: {removed} NPC hostiles.", $"Removed locally: {removed} hostile NPCs."));
    }

    // ------------------------------------------------------------------ Projectiles

    private static void DrawProjectilesTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Monitor de proyectiles", "Projectile monitor"));
        int active = 0;
        foreach (Projectile projectile in Main.projectile)
            if (projectile != null && projectile.active) active++;
        Utils.DrawBorderString(sb, Tr($"Proyectiles activos: {active}", $"Active projectiles: {active}"), new Vector2(panel.X + Pad, y), Color.LightGray, 0.78f);
        y += 28;
        int bw = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), Tr("Eliminar todos", "Clear all"), mouse, new Color(145, 50, 45)))
        {
            if (IsServer)
                SendCmd("/clear projectiles");
            else
            {
                int removed = 0;
                foreach (Projectile projectile in Main.projectile)
                {
                    if (projectile != null && projectile.active)
                    {
                        projectile.active = false;
                        removed++;
                    }
                }
                SetStatus(Tr($"Eliminados localmente: {removed} proyectiles.", $"Removed locally: {removed} projectiles."));
            }
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), Tr("Solo hostiles", "Hostile only"), mouse, new Color(120, 70, 45)))
        {
            if (IsServer)
            {
                SetStatus("TShock estándar solo permite limpiar proyectiles en general con /clear projectiles; no tiene limpieza solo de hostiles.");
            }
            else
            {
                int removed = 0;
                foreach (Projectile projectile in Main.projectile)
                {
                    if (projectile != null && projectile.active && !projectile.friendly)
                    {
                        projectile.active = false;
                        removed++;
                    }
                }
                SetStatus(Tr($"Eliminados localmente: {removed} proyectiles no amistosos.", $"Removed locally: {removed} non-friendly projectiles."));
            }
        }
        y += RowH + 14;
        DrawSectionHeader(sb, panel, ref y, Tr("Primeros proyectiles activos", "First active projectiles"));
        int shown = 0;
        for (int i = 0; i < Main.projectile.Length && shown < 12; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile == null || !projectile.active) continue;
            if (y + 22 > panel.Bottom - 40) break;
            string line = $"#{i}  tipo {projectile.type}  daño {projectile.damage}  dueño {projectile.owner}";
            Utils.DrawBorderString(sb, line, new Vector2(panel.X + Pad, y + 3), Color.White, 0.66f);
            y += 22;
            shown++;
        }
        if (shown == 0)
            Utils.DrawBorderString(sb, Tr("No hay proyectiles activos.", "No active projectiles."), new Vector2(panel.X + Pad, y), Color.Gray, 0.7f);
        Utils.DrawBorderString(sb, Tr("Las acciones locales no garantizan cambios en un servidor remoto.", "Local actions do not guarantee changes on a remote server."), new Vector2(panel.X + Pad, panel.Bottom - 36), new Color(230, 190, 100), 0.58f);
    }

    private static void DrawBuffEditorTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Editor de Buffs por ID", "Buff Editor by ID"));
        Utils.DrawBorderString(sb, Tr("ID del buff:", "Buff ID:"), new Vector2(panel.X + Pad, y), Color.White, 0.75f);
        Utils.DrawBorderString(sb, Tr("Duración (segundos):", "Duration (seconds):"), new Vector2(panel.X + Pad + 190, y), Color.White, 0.75f);
        y += 20;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 170, RowH), 4, buffIdText, mouse, "1");
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad + 190, y, 170, RowH), 5, buffDurationText, mouse, "600");
        y += RowH + 8;
        int bw = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), Tr("Aplicar buff", "Apply buff"), mouse, new Color(45, 115, 70)))
            ApplyBuffById();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), Tr("Quitar buff por ID", "Remove buff by ID"), mouse, new Color(140, 65, 55)))
            RemoveBuffById();
        y += RowH + 14;
        DrawSectionHeader(sb, panel, ref y, Tr("Ejemplos de IDs", "Example IDs"));
        string examples = "1 Regeneration | 3 Swiftness | 5 Ironskin | 6 Mana Regeneration |  shine = " + BuffID.Shine;
        Utils.DrawBorderString(sb, examples, new Vector2(panel.X + Pad, y), Color.LightGray, 0.62f);
        y += 24;
        Utils.DrawBorderString(sb, Tr("En servidores, la autoridad puede bloquear cambios del cliente.", "Servers may reject client-side buff changes."),
            new Vector2(panel.X + Pad, y), new Color(220, 190, 110), 0.62f);
    }

    private static void ApplyBuffById()
    {
        if (!int.TryParse(buffIdText, out int id) || id <= 0 || id >= BuffID.Count)
        {
            SetStatus(Tr("ID de buff no válido.", "Invalid buff ID."));
            return;
        }
        if (!int.TryParse(buffDurationText, out int seconds) || seconds <= 0)
        {
            SetStatus(Tr("La duración debe ser mayor que cero.", "Duration must be greater than zero."));
            return;
        }
        seconds = Math.Min(seconds, 86400);
        if (IsServer)
        {
            SendCmd($"/buff {id} {seconds}");
            return;
        }
        Main.LocalPlayer.AddBuff(id, seconds * 60);
        SyncSelf();
        SetStatus($"{Lang.GetBuffName(id)} (ID {id}) — {seconds}s");
    }

    private static void RemoveBuffById()
    {
        if (!int.TryParse(buffIdText, out int id) || id <= 0 || id >= BuffID.Count)
        {
            SetStatus(Tr("ID de buff no válido.", "Invalid buff ID."));
            return;
        }
        if (IsServer)
        {
            SetStatus("TShock no incluye /removebuff de forma estándar; el buff solo puede quitarse localmente en un jugador.");
            return;
        }
        Player p = Main.LocalPlayer;
        for (int i = p.buffType.Length - 1; i >= 0; i--)
            if (p.buffType[i] == id) p.DelBuff(i);
        SyncSelf();
        SetStatus(Tr("Buff eliminado si estaba activo.", "Buff removed if it was active."));
    }

    private static void DrawPermissionsTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Comandos TShock", "TShock commands"));
        string[] lines = spanish ? new[]
        {
            "El panel envía comandos normales de TShock; no usa /heros.",
            "TShock valida los permisos de tu cuenta en el servidor.",
            "",
            "/item <id> <cantidad> — darte objetos",
            "/buff <id> <segundos> — aplicar buff propio",
            "/spawnmob <id> <cantidad> — invocar NPC",
            "/spawnboss <nombre> — invocar jefe (si está disponible)",
            "/godmode — alternar god mode de TShock",
            "/heal [jugador] — curar",
            "/butcher — eliminar NPC hostiles",
            "/time day|night|noon|midnight — cambiar hora",
            "/tppos <x> <y>, /tp <jugador>, /tphere <jugador>",
            "/clear projectiles — eliminar proyectiles",
            "",
            "TShock estándar no incluye /stats, /rain ni /removebuff.",
            "Si un comando falla, revisa /help y los permisos TShock."
        } : new[]
        {
            "The panel sends regular TShock commands; it does not use /heros.",
            "TShock validates your account permissions on the server.",
            "",
            "/item <id> <amount> — give yourself items",
            "/buff <id> <seconds> — apply a self buff",
            "/spawnmob <id> <amount> — spawn NPCs",
            "/spawnboss <name> — spawn a boss (if supported)",
            "/godmode — toggle TShock god mode",
            "/heal [player] — heal",
            "/butcher — remove hostile NPCs",
            "/time day|night|noon|midnight — change time",
            "/tppos <x> <y>, /tp <player>, /tphere <player>",
            "/clear projectiles — clear projectiles",
            "",
            "Standard TShock has no /stats, /rain or /removebuff command.",
            "If a command fails, check /help and your TShock permissions."
        };
        foreach (string line in lines)
        {
            Utils.DrawBorderString(sb, line, new Vector2(panel.X + Pad, y), line.StartsWith("/") ? Color.LightSkyBlue : Color.White, 0.67f);
            y += 20;
        }
    }

    // ------------------------------------------------------------------ Teleport

    private static void DrawTeleportTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Mapa y puntos de teletransporte", "Map and teleport points"));
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
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + btnW + 8, y, btnW, RowH), Tr("Recargar mapa", "Refresh map"), mouse, new Color(75, 90, 125)))
        {
            worldMapNeedsRefresh = true;
            SetStatus(Tr("Mapa marcado para actualizar.", "Map queued for refresh."));
        }
        y += RowH + 8;

        Utils.DrawBorderString(sb, Tr("Mapa del mundo: haz clic para teletransportarte", "World map: click to teleport"), new Vector2(panel.X + Pad, y), Color.Gold, 0.75f);
        y += 20;
        int mapW = Width - Pad * 2;
        int mapH = Math.Min(360, Math.Max(220, panel.Bottom - y - 44));
        var mapRect = new Rectangle(panel.X + Pad, y, mapW, mapH);
        sb.Draw(pixel, mapRect, new Color(15, 20, 30));
        EnsureWorldMapTexture(sb);
        if (worldMapTexture != null)
        {
            // Fit map into panel while preserving world aspect ratio.
            float scale = Math.Min((float)mapRect.Width / MapTextureWidth, (float)mapRect.Height / MapTextureHeight);
            int drawW = (int)(MapTextureWidth * scale);
            int drawH = (int)(MapTextureHeight * scale);
            var drawRect = new Rectangle(mapRect.X + (mapRect.Width - drawW) / 2, mapRect.Y + (mapRect.Height - drawH) / 2, drawW, drawH);
            sb.Draw(worldMapTexture, drawRect, Color.White);
            // Cuadrícula y referencias visuales para orientar el mapa.
            for (int i = 1; i < 4; i++)
            {
                int gx = drawRect.X + drawRect.Width * i / 4;
                int gy = drawRect.Y + drawRect.Height * i / 4;
                sb.Draw(pixel, new Rectangle(gx, drawRect.Y, 1, drawRect.Height), Color.White * 0.22f);
                sb.Draw(pixel, new Rectangle(drawRect.X, gy, drawRect.Width, 1), Color.White * 0.22f);
            }
            int surfaceY = drawRect.Y + (int)(Math.Clamp((float)(Main.worldSurface / Math.Max(1f, Main.maxTilesY)), 0f, 1f) * drawRect.Height);
            sb.Draw(pixel, new Rectangle(drawRect.X, surfaceY, drawRect.Width, 2), new Color(235, 220, 130) * 0.8f);
            Utils.DrawBorderString(sb, Tr("SUPERFICIE", "SURFACE"), new Vector2(drawRect.X + 5, Math.Max(drawRect.Y + 2, surfaceY - 17)), Color.White, 0.55f);
            Utils.DrawBorderString(sb, Tr("INFIERNO", "UNDERWORLD"), new Vector2(drawRect.X + 5, drawRect.Bottom - 16), Color.Orange, 0.55f);
            // Mark current player position.
            int px = Math.Clamp((int)(Main.LocalPlayer.Center.X / 16f / Math.Max(1, Main.maxTilesX) * drawRect.Width), 0, drawRect.Width - 1);
            int py = Math.Clamp((int)(Main.LocalPlayer.Center.Y / 16f / Math.Max(1, Main.maxTilesY) * drawRect.Height), 0, drawRect.Height - 1);
            sb.Draw(pixel, new Rectangle(drawRect.X + px - 3, drawRect.Y + py - 3, 7, 7), Color.Yellow);
            if (Clicked && drawRect.Contains(mouse))
            {
                Main.mouseLeftRelease = false;
                int tileX = Math.Clamp((int)((mouse.X - drawRect.X) / (float)drawRect.Width * Main.maxTilesX), 10, Main.maxTilesX - 10);
                int tileY = Math.Clamp((int)((mouse.Y - drawRect.Y) / (float)drawRect.Height * Main.maxTilesY), 10, Main.maxTilesY - 10);
                if (IsServer)
                {
                    SendCmd($"/tppos {tileX} {tileY}");
                }
                else
                {
                    Point safe = FindSafeMapDestination(tileX, tileY);
                    TeleportPlayer(safe.X * 16, safe.Y * 16, $"Mapa ({safe.X}, {safe.Y})");
                }
            }
        }
        else
        {
            Utils.DrawBorderString(sb, Tr("No se pudo crear el mapa; pulsa Recargar mapa.", "Map unavailable; press Refresh map."), new Vector2(mapRect.X + 8, mapRect.Y + 8), Color.White, 0.7f);
        }
        y += mapH + 4;
        Utils.DrawBorderString(sb, Tr("Amarillo = tu posición. En multijugador el servidor puede rechazar el TP.", "Yellow = your position. Multiplayer servers may reject teleport."), new Vector2(panel.X + Pad, y), Color.LightGray, 0.62f);
    }

    private static void EnsureWorldMapTexture(SpriteBatch sb)
    {
        if (!worldMapNeedsRefresh && worldMapTexture != null)
            return;

        try
        {
            if (worldMapTexture == null || worldMapTexture.IsDisposed || worldMapTexture.GraphicsDevice != sb.GraphicsDevice)
            {
                worldMapTexture?.Dispose();
                worldMapTexture = new Texture2D(sb.GraphicsDevice, MapTextureWidth, MapTextureHeight, false, SurfaceFormat.Color);
            }

            worldMapPixels ??= new Color[MapTextureWidth * MapTextureHeight];
            for (int py = 0; py < MapTextureHeight; py++)
            {
                int tileY = Math.Clamp(py * Main.maxTilesY / MapTextureHeight, 0, Main.maxTilesY - 1);
                for (int px = 0; px < MapTextureWidth; px++)
                {
                    int tileX = Math.Clamp(px * Main.maxTilesX / MapTextureWidth, 0, Main.maxTilesX - 1);
                    var tile = Main.tile[tileX, tileY];
                    Color color;
                    if (tile == null)
                        color = new Color(25, 30, 45);
                    else if (tile.LiquidAmount > 0)
                        color = new Color(45, 105, 190);
                    else if (tile.HasTile)
                    {
                        int type = tile.TileType;
                        if (type == TileID.Dirt || type == TileID.Mud) color = new Color(115, 78, 50);
                        else if (type == TileID.Grass || type == TileID.JungleGrass) color = new Color(55, 145, 65);
                        else if (type == TileID.Stone) color = new Color(105, 105, 115);
                        else if (type == TileID.Sand) color = new Color(205, 185, 120);
                        else if (type == TileID.SnowBlock || type == TileID.IceBlock) color = new Color(180, 215, 235);
                        else if (type == TileID.Ash || type == TileID.Hellstone) color = new Color(155, 75, 45);
                        else color = new Color(85, 115, 95);
                    }
                    else if (tile.WallType > 0)
                        color = new Color(65, 60, 75);
                    else
                        color = tileY < Main.worldSurface ? new Color(70, 130, 185) : new Color(35, 35, 48);

                    worldMapPixels[py * MapTextureWidth + px] = color;
                }
            }
            worldMapTexture.SetData(worldMapPixels);
            worldMapNeedsRefresh = false;
        }
        catch
        {
            worldMapNeedsRefresh = true;
        }
    }

    private static Point FindSafeMapDestination(int tileX, int tileY)
    {
        tileX = Math.Clamp(tileX, 10, Main.maxTilesX - 10);
        tileY = Math.Clamp(tileY, 10, Main.maxTilesY - 10);
        // Search downward first for a solid floor with room for the player.
        for (int y = tileY; y < Math.Min(Main.maxTilesY - 4, tileY + 100); y++)
        {
            var floor = Main.tile[tileX, y];
            if (floor != null && floor.HasTile && Main.tileSolid[floor.TileType] && !Main.tileSolidTop[floor.TileType]
                && !Main.tile[tileX, y - 1].HasTile && !Main.tile[tileX, y - 2].HasTile && !Main.tile[tileX, y - 3].HasTile)
                return new Point(tileX, y);
        }
        // Otherwise search upward from the clicked point for open space.
        for (int y = tileY; y > 5; y--)
        {
            if (!Main.tile[tileX, y].HasTile && !Main.tile[tileX, y - 1].HasTile && !Main.tile[tileX, y - 2].HasTile)
                return new Point(tileX, y - 2);
        }
        return new Point(tileX, Math.Clamp(tileY, 10, Main.maxTilesY - 10));
    }

    // ------------------------------------------------------------------ Jugadores

    private static Rectangle Slot(int startX, int rowY, int w, int gap, int k)
    {
        return new Rectangle(startX + k * (w + gap), rowY, w, RowH);
    }

    private static void DrawPlayersTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Jugadores Conectados (Moderación)");

        int nameW = 120;
        int gap = 4;
        int actionW = (Width - (Pad * 2) - nameW - gap * 6) / 7;
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
            string label = isSelf ? $"{target.name} (Tú)" : target.name;
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
                    if (IsServer)
                        SendCmd($"/heal {Quote(target.name)}");
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
                    SetStatus(Tr("Mute nativo aún requiere añadir el filtro de chat del servidor.", "Native mute still requires the server chat filter."));
                }

                if (DrawButton(sb, pixel, Slot(startX, y, actionW, gap, 6), "Ban", mouse, new Color(145, 45, 45)))
                {
                    SetStatus(Tr("Ban permanente requiere integrar la lista de bloqueos al aceptar conexiones.", "Permanent ban requires integrating a ban list into connection acceptance."));
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
            ? "Modo NATIVO: el servidor comprueba la lista HerosModAdmins.txt. Kick se ejecuta en servidor."
            : (InMultiplayer
                ? "Modo NATIVO: las acciones compatibles se ejecutan en el servidor Unified."
                : "Las acciones de moderación requieren que el servidor las implemente y autorice.");
        Utils.DrawBorderString(sb, note, new Vector2(panel.X + Pad, panel.Bottom - 46), new Color(200, 180, 100), 0.65f);
    }

    // ------------------------------------------------------------------ dibujo comun

    private static void DrawSettingsTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, Tr("Personalización del panel", "Panel customization"));
        Utils.DrawBorderString(sb, Tr("Tamaño de la interfaz", "Interface size"), new Vector2(panel.X + Pad, y + 3), Color.White, 0.72f);
        int controlY = y + 24;
        int half = (panel.Width - Pad * 2 - 12) / 3;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, controlY, half, RowH), "−  Reducir", mouse))
        {
            uiScale = Math.Clamp(MathF.Round(uiScale - 0.05f, 2), 0.85f, 1.25f);
            ClampPanelPosition();
            SetStatus($"Tamaño UI: {uiScale:P0}");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + half + 6, controlY, half, RowH), $"{uiScale:P0}", mouse, AccentColor))
        {
            uiScale = 1.0f;
            ClampPanelPosition();
            SetStatus(Tr("Tamaño restaurado al 100 %", "Size reset to 100%"));
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + (half + 6) * 2, controlY, half, RowH), "＋  Agrandar", mouse))
        {
            uiScale = Math.Clamp(MathF.Round(uiScale + 0.05f, 2), 0.85f, 1.25f);
            ClampPanelPosition();
            SetStatus($"Tamaño UI: {uiScale:P0}");
        }
        y += RowH + 34;

        Utils.DrawBorderString(sb, Tr("Color principal", "Accent color"), new Vector2(panel.X + Pad, y + 3), Color.White, 0.72f);
        y += 23;
        int colorW = (panel.Width - Pad * 2 - 4 * 8) / themeColors.Length;
        for (int i = 0; i < themeColors.Length; i++)
        {
            var rect = new Rectangle(panel.X + Pad + i * (colorW + 8), y, colorW, 34);
            sb.Draw(pixel, rect, themeColors[i]);
            if (i == themeIndex)
            {
                sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 3), Color.White);
                sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 3, rect.Width, 3), Color.White);
            }
            if (rect.Contains(mouse) && Clicked)
            {
                Main.mouseLeftRelease = false;
                themeIndex = i;
                SetStatus(Tr("Color del panel actualizado.", "Panel color updated."));
            }
        }
        y += 48;

        DrawSectionHeader(sb, panel, ref y, Tr("Mejoras del mapa", "Map improvements"));
        string[] mapInfo =
        {
            Tr("• Mapa de mayor resolución y cuadrícula de orientación.", "• Higher-resolution map with a guide grid."),
            Tr("• Línea de superficie y posición actual en amarillo.", "• Surface line and current position in yellow."),
            Tr("• Haz clic en el mapa para elegir destino.", "• Click the map to choose a destination."),
            Tr("• Pulsa Recargar mapa después de modificar el mundo.", "• Refresh the map after world changes.")
        };
        foreach (string line in mapInfo)
        {
            Utils.DrawBorderString(sb, line, new Vector2(panel.X + Pad, y), Color.LightGray, 0.66f);
            y += 22;
        }

        y += 8;
        DrawSectionHeader(sb, panel, ref y, Tr("Herramientas rápidas", "Quick tools"));
        int bw = (panel.Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), Tr("Actualizar mapa", "Refresh map"), mouse, AccentColor))
        {
            worldMapNeedsRefresh = true;
            SetStatus(Tr("Mapa pendiente de actualizar.", "Map refresh queued."));
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), Tr("Restaurar UI", "Reset UI"), mouse))
        {
            uiScale = 1.0f;
            themeIndex = 0;
            ClampPanelPosition();
            SetStatus(Tr("Tamaño y color restaurados.", "Size and color reset."));
        }
    }

    private static void ClampPanelPosition()
    {
        position.X = Math.Clamp(position.X, 0f, Math.Max(0f, Main.screenWidth - Width));
        position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, Main.screenHeight - Height));
    }

    private static void DrawSectionHeader(SpriteBatch sb, Rectangle panel, ref int y, string title)
    {
        Utils.DrawBorderString(sb, title, new Vector2(panel.X + Pad, y), AccentColor, 0.8f);
        y += 22;
    }

    private static bool DrawButton(SpriteBatch sb, Texture2D pixel, Rectangle rect, string text, Point mouse, Color? customBg = null)
    {
        bool hover = rect.Contains(mouse);
        Color bg = hover ? new Color(76, 88, 112) : (customBg ?? new Color(39, 45, 57));
        sb.Draw(pixel, rect, bg);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y + rect.Height - 2, rect.Width, 2), hover ? AccentColor : new Color(18, 21, 28));
        if (hover)
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height - 2), AccentColor);

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

        bool grave = Main.keyState.IsKeyDown(Keys.OemTilde) && Main.oldKeyState.IsKeyUp(Keys.OemTilde);
        if (grave)
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
        else if (activeField == 4)
            buffIdText = Main.GetInputText(buffIdText);
        else if (activeField == 5)
            buffDurationText = Main.GetInputText(buffDurationText);
        else if (activeField == 6)
            itemDamageText = Main.GetInputText(itemDamageText);
        else if (activeField == 7)
            itemDefenseText = Main.GetInputText(itemDefenseText);
        else if (activeField == 8)
            editorSlotText = Main.GetInputText(editorSlotText);
        else if (activeField == 9)
            editorStackText = Main.GetInputText(editorStackText);
        else if (activeField == 10)
            editorDamageText = Main.GetInputText(editorDamageText);
        else if (activeField == 11)
            editorDefenseText = Main.GetInputText(editorDefenseText);
        else if (activeField == 12)
            editorCritText = Main.GetInputText(editorCritText);
        else if (activeField == 13)
            editorRarityText = Main.GetInputText(editorRarityText);
        else if (activeField == 14)
            editorKnockbackText = Main.GetInputText(editorKnockbackText);
        else if (activeField == 15)
            editorPrefixText = Main.GetInputText(editorPrefixText);
        else if (activeField == 16)
            playerLifeText = Main.GetInputText(playerLifeText);
        else if (activeField == 17)
            playerMaxLifeText = Main.GetInputText(playerMaxLifeText);
        else if (activeField == 18)
            playerManaText = Main.GetInputText(playerManaText);
        else if (activeField == 19)
            playerMaxManaText = Main.GetInputText(playerMaxManaText);
        else if (activeField == 20)
            editorUseTimeText = Main.GetInputText(editorUseTimeText);
        else if (activeField == 21)
            editorUseAnimationText = Main.GetInputText(editorUseAnimationText);
        else if (activeField == 22)
            editorUseStyleText = Main.GetInputText(editorUseStyleText);
        else if (activeField == 23)
            editorManaText = Main.GetInputText(editorManaText);
        else if (activeField == 24)
            editorShootText = Main.GetInputText(editorShootText);
        else if (activeField == 25)
            editorShootSpeedText = Main.GetInputText(editorShootSpeedText);
        else if (activeField == 26)
            editorScaleText = Main.GetInputText(editorScaleText);
        else if (activeField == 27)
            editorValueText = Main.GetInputText(editorValueText);
        else if (activeField == 28)
            npcQuery = Main.GetInputText(npcQuery);
        else if (activeField == 29)
            npcAmountText = Main.GetInputText(npcAmountText);
        else if (activeField == 30)
            editorPickText = Main.GetInputText(editorPickText);
        else if (activeField == 31)
            editorAxeText = Main.GetInputText(editorAxeText);
        else if (activeField == 32)
            editorHealLifeText = Main.GetInputText(editorHealLifeText);
        else if (activeField == 33)
            editorHealManaText = Main.GetInputText(editorHealManaText);
        else if (activeField == 34)
            editorAmmoText = Main.GetInputText(editorAmmoText);
        else if (activeField == 35)
            editorUseAmmoText = Main.GetInputText(editorUseAmmoText);
        else if (activeField == 36)
            editorCreateTileText = Main.GetInputText(editorCreateTileText);
        else if (activeField == 37)
            editorCreateWallText = Main.GetInputText(editorCreateWallText);
        else if (activeField == 38)
            editorBuffTypeText = Main.GetInputText(editorBuffTypeText);
        else if (activeField == 39)
            editorBuffTimeText = Main.GetInputText(editorBuffTimeText);
        else if (activeField == 40)
            editorMountTypeText = Main.GetInputText(editorMountTypeText);
        else if (activeField == 8101)
            weTileIdText = FilterDigits(Main.GetInputText(weTileIdText), 6);
        else if (activeField == 8102)
            weReplaceFromText = FilterDigits(Main.GetInputText(weReplaceFromText), 6);
        else if (activeField == 8103)
            weReplaceToText = FilterDigits(Main.GetInputText(weReplaceToText), 6);

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

    private static void SendHerosCmd(string action)
    {
        if (Main.netMode != 1)
        {
            SetStatus("El comando remoto /heros requiere un servidor Unified con HerosModServerCommands integrado.");
            return;
        }
        string payload = "/heros " + (action ?? string.Empty).Trim();
        ChatHelper.SendChatMessageFromClient(ChatManager.Commands.CreateOutgoingMessage(payload));
        SetStatus("Comando enviado al servidor: " + payload);
    }

    private static void SendCmd(string command)
    {
        // Seguridad: este panel no ejecuta ni envia comandos de chat a TShock
        // ni a otros servidores. Las acciones remotas requieren una API nativa
        // autorizada; no se intenta simularlas con texto en el chat.
        SetStatus("Comandos externos desactivados. Esta accion no tiene implementacion nativa remota.");
    }

    /// <summary>Acción que todavía no tiene implementación nativa en el servidor.</summary>
    private static void NotInServer(string what)
    {
        SetStatus($"{what}: solo en un jugador o en modo DIRECTO (arriba).");
    }

    /// <summary>Acción remota que aún no se implementa en el manejador nativo.</summary>
    private static void NeedsCommands(string what)
    {
        SetStatus($"{what}: esta acción no está disponible como comando estándar de TShock.");
    }

    /// <summary>Acción de mundo sin implementación nativa.</summary>
    private static void NoRemoteWorld(string what)
    {
        SetStatus($"{what}: esta acción no está disponible como comando estándar de TShock.");
    }

    private static void ToggleToolMode()
    {
        toolMode = toolMode switch
        {
            ToolMode.Normal => ToolMode.Debug,
            ToolMode.Debug => ToolMode.Cheat,
            _ => ToolMode.Normal
        };

        string description = toolMode switch
        {
            ToolMode.Normal => "Modo NORMAL activado.",
            ToolMode.Debug => "Modo DEBUG activado: perfil de diagnóstico del panel.",
            ToolMode.Cheat => "Modo CHEAT activado: herramientas locales/administrativas disponibles según el entorno.",
            _ => "Modo NORMAL activado."
        };
        SetStatus(description);
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
            SetStatus($"No se encontró el objeto: '{itemQuery}'");
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
            // TShock /item: crear el objeto en el inventario propio.
            string cmd = $"/item {id} {count}";

            SendCmd(cmd);
            return;
        }

        var p = Main.LocalPlayer;
        var source = new PanelItemSource();
        int itemIndex = Item.NewItem(source, (int)p.position.X, (int)p.position.Y, p.width, p.height, id, count, false, prefixValue);

        // Terraria Unified expone Main.item como WorldItem y no ofrece aquí
        // acceso compatible a los campos damage/defense del objeto generado.
        // Se conserva la generación del objeto; daño/defensa personalizados
        // quedan desactivados para evitar errores de compilación.
        SetStatus(InMultiplayer
            ? Tr("Objeto generado localmente; el servidor puede rechazarlo.", "Item spawned locally; the server may reject it.")
            : $"Objeto: {Lang.GetItemNameValue(id)} x{count} (daño/defensa opcionales)");
    }

    private static void SpawnBoss(int npcId, string bossName, string tshockName)
    {
        if (IsServer)
        {
            SendCmd($"/spawnmob {npcId} 1");
            return;
        }

        NPC.SpawnOnPlayer(Main.myPlayer, npcId);
        SetStatus($"¡{bossName} ha sido invocado!");
    }

    private static void TeleportPlayer(int worldX, int worldY, string destination)
    {
        var p = Main.LocalPlayer;
        p.Teleport(new Vector2(worldX, worldY - p.height), 1);
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
        SetStatus($"{target.name} fue traído hacia ti.");
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