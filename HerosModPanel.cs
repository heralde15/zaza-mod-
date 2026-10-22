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
internal static partial class HerosModPanel
{
    private enum Tab { Jugador, Mundo, Items, EditorItems, EditorVida, WorldEdit, Jefes, Teleport, Jugadores, Herramientas, Info, Inventario, Guia, Perfiles, Personalizar }

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
    // Apariencia del panel; solo cambia el dibujo, no altera las funciones del mod.
    private static int uiTheme = 0;
    private static bool uiHighContrast;
    private static readonly Color[] UiAccents =
    {
        new Color(70, 130, 180), // Azul
        new Color(80, 165, 115), // Verde
        new Color(160, 105, 205), // Violeta
        new Color(210, 125, 65), // Naranja
        new Color(205, 80, 95), // Rojo
    };
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
    // Herramientas adicionales de moderación/comandos (requieren servidor con TShock/plugins).
    private static string moderationTarget = "";
    private static string moderationReason = "";
    private static int toolsPage;
    private static string editorSlot = "1";
    private static string editorDamage = "";
    private static string editorDefense = "";
    private static string editorUseTime = "";
    private static string editorStack = "";
    private static string editorValue = "";
    private static string editorType = "1";
    private static string editorPrefix = "0";
    private static string editorKnockback = "";
    private static string editorCrit = "";
    private static string editorUseAnimation = "";
    private static string editorShootSpeed = "";
    private static string editorProjectile = "0";
    private static string editorRarity = "";
    private static string teleportX = "";
    private static string teleportY = "";
    private static bool worldEditSetPos1Next = true;
    private static string lifeCurrent = "";
    private static string lifeMaximum = "";
    private static string manaCurrent = "";
    private static string manaMaximum = "";
    private static Point? worldPos1;
    private static Point? worldPos2;
    private static string worldTileType = "1";
    private static string worldReplaceType = "0";
    private static int worldEditUndoType = -1;
    private static readonly List<(int x, int y, Tile tile)> worldEditUndo = new();

    private static readonly (string label, string command)[] extraTools = new (string, string)[]
    {
        ("Kick jugador", "/kick {target} {reason}"),
        ("Ban jugador", "/ban {target} {reason}"),
        ("Silenciar", "/mute {target}"),
        ("Quitar silencio", "/unmute {target}"),
        ("Teletransportarme", "/tp {target}"),
        ("Traer jugador", "/tphere {target}"),
        ("Eliminar jugador", "/kill {target}"),
        ("Curar jugador", "/heal {target}"),
        ("Slap / empujar", "/slap {target}"),
        ("Ver usuario", "/userinfo {target}"),
        ("Lista de jugadores", "/who"),
        ("Quién está jugando", "/playing"),
        ("Ver reglas", "/rules"),
        ("Mensaje del día", "/motd"),
        ("Ayuda del servidor", "/help"),
        ("Ir al spawn", "/spawn"),
        ("Volver a casa", "/home"),
        ("Volver atrás", "/back"),
        ("Listar warps", "/warp list"),
        ("Usar warp", "/warp {target}"),
        ("Eliminar enemigos", "/butcher"),
        ("Asentar líquidos", "/settle"),
        ("Poner de día", "/time day"),
        ("Poner de noche", "/time night"),
        ("Poner mediodía", "/time noon"),
        ("Poner medianoche", "/time midnight"),
        ("Iniciar lluvia", "/rain start"),
        ("Detener lluvia", "/rain stop"),
        ("Luna de sangre", "/bloodmoon"),
        ("Eclipse solar", "/eclipse"),
        ("Invasión goblin", "/invasion goblin"),
        ("Invasión helada", "/invasion frost"),
        ("Invasión pirata", "/invasion pirate"),
        ("Invasión marciana", "/invasion martian"),
        ("Luna de calabaza", "/invasion pumpkin"),
        ("Luna helada", "/invasion frostmoon"),
        ("Evento Solar", "/invasion solar"),
        ("Evento Vortex", "/invasion vortex"),
        ("Evento Nebula", "/invasion nebula"),
        ("Evento Stardust", "/invasion stardust"),
        ("Crecer plantas", "/grow"),
        ("Invocar meteorito", "/dropmeteor"),
        ("Activar Hardmode", "/hardmode"),
        ("Listar grupos", "/group list"),
        ("Listar usuarios", "/user list"),
        ("Listar regiones", "/region list"),
        ("Info de región", "/region info {target}"),
        ("Cambiar clave servidor", "/serverpassword {target}"),
        ("Buscar actualizaciones", "/checkupdates"),
        ("Info del mundo", "/world"),
        ("MODERACIÓN · Kick jugador", "/kick {target} {reason}"),
        ("MODERACIÓN · Ban jugador", "/ban {target} {reason}"),
        ("MODERACIÓN · Silenciar", "/mute {target}"),
        ("MODERACIÓN · Quitar silencio", "/unmute {target}"),
        ("MODERACIÓN · Perfil jugador", "/userinfo {target}"),
        ("MODERACIÓN · Lista conectados", "/playing"),
        ("MODERACIÓN · Kick global (plugin)", "/kickall {reason}"),
        ("MODERACIÓN · Advertencia (plugin)", "/warn {target} {reason}"),
        ("MODERACIÓN · Historial (plugin)", "/history {target}"),
        ("MODERACIÓN · Ayuda moderación", "/help"),
        ("USUARIOS · Listar usuarios", "/user list"),
        ("USUARIOS · Ver usuario", "/user info {target}"),
        ("USUARIOS · Crear usuario (admin)", "/user add {target}"),
        ("USUARIOS · Eliminar usuario (admin)", "/user del {target}"),
        ("USUARIOS · Listar grupos", "/group list"),
        ("USUARIOS · Ver grupo (plugin)", "/group {target}"),
        ("USUARIOS · Asignar grupo (plugin)", "/user group {target}"),
        ("USUARIOS · Permisos grupo (plugin)", "/group perm {target}"),
        ("USUARIOS · Añadir permiso (plugin)", "/group addperm {target}"),
        ("USUARIOS · Quitar permiso (plugin)", "/group delperm {target}"),
        ("REGIONES · Listar regiones", "/region list"),
        ("REGIONES · Info región", "/region info {target}"),
        ("REGIONES · Proteger región (plugin)", "/region protect {target}"),
        ("REGIONES · Eliminar región (admin)", "/region delete {target}"),
        ("REGIONES · Ajustar región (plugin)", "/region set {target}"),
        ("REGIONES · Añadir miembro (plugin)", "/region add {target}"),
        ("REGIONES · Quitar miembro (plugin)", "/region remove {target}"),
        ("REGIONES · Listar regiones", "/region list"),
        ("REGIONES · Ver protección (plugin)", "/region info {target}"),
        ("REGIONES · Ayuda regiones", "/help region"),
        ("TIEMPO · Poner día", "/time day"),
        ("TIEMPO · Poner noche", "/time night"),
        ("TIEMPO · Mediodía", "/time noon"),
        ("TIEMPO · Medianoche", "/time midnight"),
        ("TIEMPO · Consultar hora (plugin)", "/time"),
        ("TIEMPO · Hora personalizada (plugin)", "/time {target}"),
        ("TIEMPO · Restablecer día", "/time day"),
        ("TIEMPO · Cambiar a noche", "/time night"),
        ("TIEMPO · Ayuda tiempo", "/help time"),
        ("TIEMPO · Info mundo", "/world"),
        ("CLIMA · Iniciar lluvia", "/rain start"),
        ("CLIMA · Detener lluvia", "/rain stop"),
        ("CLIMA · Clima (plugin)", "/rain"),
        ("CLIMA · Iniciar tormenta", "/rain start"),
        ("CLIMA · Parar tormenta", "/rain stop"),
        ("CLIMA · Luna de sangre", "/bloodmoon"),
        ("CLIMA · Eclipse solar", "/eclipse"),
        ("CLIMA · Info clima (plugin)", "/world"),
        ("CLIMA · Ayuda clima", "/help rain"),
        ("CLIMA · Estado mundo", "/world"),
        ("EVENTOS · Invasión goblin", "/invasion goblin"),
        ("EVENTOS · Invasión pirata", "/invasion pirate"),
        ("EVENTOS · Invasión helada", "/invasion frost"),
        ("EVENTOS · Invasión marciana", "/invasion martian"),
        ("EVENTOS · Luna calabaza", "/invasion pumpkin"),
        ("EVENTOS · Luna helada", "/invasion frostmoon"),
        ("EVENTOS · Evento Solar", "/invasion solar"),
        ("EVENTOS · Evento Vortex", "/invasion vortex"),
        ("EVENTOS · Evento Nebula", "/invasion nebula"),
        ("EVENTOS · Evento Stardust", "/invasion stardust"),
        ("PROGRESIÓN · Activar Hardmode", "/hardmode"),
        ("PROGRESIÓN · Crecer plantas", "/grow"),
        ("PROGRESIÓN · Invocar meteorito", "/dropmeteor"),
        ("PROGRESIÓN · Asentar líquidos", "/settle"),
        ("PROGRESIÓN · Eliminar enemigos", "/butcher"),
        ("PROGRESIÓN · Progreso (plugin)", "/progress"),
        ("PROGRESIÓN · Estado mundo", "/world"),
        ("PROGRESIÓN · Ayuda progresión", "/help"),
        ("PROGRESIÓN · Info mundo", "/world"),
        ("PROGRESIÓN · Actualizar datos (plugin)", "/world"),
        ("TELETRANSPORTE · Ir al spawn", "/spawn"),
        ("TELETRANSPORTE · Volver a casa", "/home"),
        ("TELETRANSPORTE · Volver atrás", "/back"),
        ("TELETRANSPORTE · Listar warps", "/warp list"),
        ("TELETRANSPORTE · Usar warp", "/warp {target}"),
        ("TELETRANSPORTE · Teletransportar", "/tp {target}"),
        ("TELETRANSPORTE · Traer jugador", "/tphere {target}"),
        ("TELETRANSPORTE · TP aleatorio (plugin)", "/tpr"),
        ("TELETRANSPORTE · Guardar hogar (plugin)", "/home set"),
        ("TELETRANSPORTE · Ayuda teletransporte", "/help tp"),
        ("JUGADOR · Curar jugador", "/heal {target}"),
        ("JUGADOR · Eliminar jugador", "/kill {target}"),
        ("JUGADOR · Empujar jugador", "/slap {target}"),
        ("JUGADOR · Ver perfil", "/userinfo {target}"),
        ("JUGADOR · Cambiar equipo (plugin)", "/team {target}"),
        ("JUGADOR · Dificultad (plugin)", "/difficulty"),
        ("JUGADOR · Modo dios (plugin)", "/godmode {target}"),
        ("JUGADOR · Reaparecer (plugin)", "/respawn {target}"),
        ("JUGADOR · Estadísticas (plugin)", "/stats {target}"),
        ("JUGADOR · Ayuda jugador", "/help"),
        ("OBJETOS · Dar objeto (plugin)", "/give {target}"),
        ("OBJETOS · Crear objeto (plugin)", "/item {target}"),
        ("OBJETOS · Dar monedas (plugin)", "/give {target}"),
        ("OBJETOS · Dar munición (plugin)", "/give {target}"),
        ("OBJETOS · Dar pociones (plugin)", "/give {target}"),
        ("OBJETOS · Dar materiales (plugin)", "/give {target}"),
        ("OBJETOS · Dar bloques (plugin)", "/give {target}"),
        ("OBJETOS · Dar accesorio (plugin)", "/give {target}"),
        ("OBJETOS · Dar arma (plugin)", "/give {target}"),
        ("OBJETOS · Ayuda objetos", "/help item"),
        ("NPC · Eliminar enemigos", "/butcher"),
        ("NPC · Invocar NPC (plugin)", "/spawnmob {target}"),
        ("NPC · Eliminar NPC (plugin)", "/butcher"),
        ("NPC · Invocar jefe (plugin)", "/spawnmob {target}"),
        ("NPC · Lista NPC (plugin)", "/npc list"),
        ("NPC · Limpiar NPC", "/butcher"),
        ("NPC · Spawn rate (plugin)", "/spawnrate"),
        ("NPC · Máximo NPC (plugin)", "/maxspawns"),
        ("NPC · Ayuda NPC", "/help spawnmob"),
        ("NPC · Estado mundo", "/world"),
        ("SERVIDOR · Lista jugadores", "/who"),
        ("SERVIDOR · Quién juega", "/playing"),
        ("SERVIDOR · Ver reglas", "/rules"),
        ("SERVIDOR · Mensaje del día", "/motd"),
        ("SERVIDOR · Ayuda general", "/help"),
        ("SERVIDOR · Info mundo", "/world"),
        ("SERVIDOR · Buscar actualizaciones", "/checkupdates"),
        ("SERVIDOR · Info servidor (plugin)", "/serverinfo"),
        ("SERVIDOR · Versión servidor (plugin)", "/version"),
        ("SERVIDOR · Lista comandos", "/help"),
        ("CONFIGURACIÓN · Cambiar contraseña", "/serverpassword {target}"),
        ("CONFIGURACIÓN · Recargar config (plugin)", "/reload"),
        ("CONFIGURACIÓN · Lista blanca (plugin)", "/whitelist"),
        ("CONFIGURACIÓN · Añadir lista blanca (plugin)", "/whitelist add {target}"),
        ("CONFIGURACIÓN · Quitar lista blanca (plugin)", "/whitelist remove {target}"),
        ("CONFIGURACIÓN · Cambiar MOTD (plugin)", "/motd {target}"),
        ("CONFIGURACIÓN · Editar reglas (plugin)", "/rules {target}"),
        ("CONFIGURACIÓN · Ver permisos (plugin)", "/checkperm {target}"),
        ("CONFIGURACIÓN · Ayuda admin", "/help"),
        ("CONFIGURACIÓN · Buscar actualizaciones", "/checkupdates"),
        ("ECONOMÍA · Ver saldo (plugin)", "/balance {target}"),
        ("ECONOMÍA · Consulta economía (plugin)", "/eco {target}"),
        ("ECONOMÍA · Dar dinero (plugin)", "/eco give {target}"),
        ("ECONOMÍA · Quitar dinero (plugin)", "/eco take {target}"),
        ("ECONOMÍA · Establecer saldo (plugin)", "/eco set {target}"),
        ("ECONOMÍA · Ranking dinero (plugin)", "/baltop"),
        ("ECONOMÍA · Tienda (plugin)", "/shop"),
        ("ECONOMÍA · Ver precio (plugin)", "/price {target}"),
        ("ECONOMÍA · Transferir dinero (plugin)", "/pay {target}"),
        ("ECONOMÍA · Ayuda economía (plugin)", "/help eco"),
        ("UTILIDADES · Ver ping (plugin)", "/ping {target}"),
        ("UTILIDADES · Limpiar chat (plugin)", "/clear"),
        ("UTILIDADES · Anuncio (plugin)", "/announce {target}"),
        ("UTILIDADES · Mensaje global (plugin)", "/broadcast {target}"),
        ("UTILIDADES · Ver logs (plugin)", "/logs"),
        ("UTILIDADES · Historial (plugin)", "/history {target}"),
        ("UTILIDADES · Estadísticas (plugin)", "/stats"),
        ("UTILIDADES · Lista plugins (plugin)", "/plugins"),
        ("UTILIDADES · Versión (plugin)", "/version"),
        ("UTILIDADES · Ayuda utilidades", "/help")
    };


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

        sb.Draw(pixel, panelRect, UiPanelColor);
        sb.Draw(pixel, titleRect, UiTitleColor);
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

        Tab[] tabs = [Tab.Jugador, Tab.Mundo, Tab.Items, Tab.EditorItems, Tab.EditorVida, Tab.WorldEdit, Tab.Jefes, Tab.Teleport,
                      Tab.Jugadores, Tab.Herramientas, Tab.Info, Tab.Inventario, Tab.Guia, Tab.Perfiles, Tab.Personalizar];
        int tabCols = 5;
        int tabWidth = (Width - (Pad * 2) - ((tabCols - 1) * 4)) / tabCols;
        for (int i = 0; i < tabs.Length; i++)
        {
            int row = i / tabCols;
            int col = i % tabCols;
            var tabRect = new Rectangle(panelRect.X + Pad + col * (tabWidth + 4),
                panelRect.Y + 36 + row * 27, tabWidth, 23);
            bool selected = currentTab == tabs[i];
            Color tabColor = selected ? UiAccent : UiTabColor;
            string tabLabel = tabs[i] switch
            {
                Tab.Jugadores => "Jugadores",
                Tab.Inventario => "Inventario",
                Tab.EditorItems => "Editor Items",
                Tab.EditorVida => "Editor Vida",
                Tab.WorldEdit => "WorldEdit",
                Tab.Teleport => "Teleport",
                Tab.Perfiles => "Perfiles",
                Tab.Personalizar => "Personalizar UI",
                _ => tabs[i].ToString()
            };
            if (DrawButton(sb, pixel, tabRect, tabLabel, mouse, tabColor) && !selected)
            {
                currentTab = tabs[i];
                activeField = 0;
            }
        }

        int contentY = panelRect.Y + 121;
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
            case Tab.EditorItems:
                DrawItemEditorTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.EditorVida:
                DrawLifeEditorTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.WorldEdit:
                DrawWorldEditTab(sb, pixel, panelRect, ref contentY, mouse);
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
            case Tab.Herramientas:
                DrawToolsTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Guia:
                DrawGuideTab(sb, pixel, panelRect, ref contentY);
                break;
            case Tab.Perfiles:
                DrawProfilesTab(sb, pixel, panelRect, ref contentY, mouse);
                break;
            case Tab.Personalizar:
                DrawCustomizeTab(sb, pixel, panelRect, ref contentY, mouse);
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

    // Mini mapa del panel: muestrea tiles alrededor del jugador y permite elegir coordenadas.
    private static void DrawWorldMiniMap(SpriteBatch sb, Texture2D pixel, Rectangle area, Point mouse, bool worldEdit)
    {
        sb.Draw(pixel, area, new Color(15, 18, 24));
        int cell = 7, span = 4;
        int cols = Math.Max(1, area.Width / cell), rows = Math.Max(1, area.Height / cell);
        int cx = (int)(Main.LocalPlayer.Center.X / 16f), cy = (int)(Main.LocalPlayer.Center.Y / 16f);
        int left = cx - cols * span / 2, top = cy - rows * span / 2;
        for (int gy = 0; gy < rows; gy++)
        for (int gx = 0; gx < cols; gx++)
        {
            int tx = Math.Clamp(left + gx * span, 1, Main.maxTilesX - 2);
            int ty = Math.Clamp(top + gy * span, 1, Main.maxTilesY - 2);
            Tile t = Main.tile[tx, ty];
            Color c = new Color(35, 40, 48);
            if (t != null && t.HasTile)
                c = t.TileType switch
                {
                    TileID.Dirt or TileID.Grass or TileID.CorruptGrass or TileID.CrimsonGrass or TileID.JungleGrass => new Color(83,145,70),
                    TileID.Stone or TileID.Ebonstone or TileID.Crimstone => new Color(105,105,115),
                    TileID.Sand or TileID.HardenedSand => new Color(205,184,118),
                    TileID.SnowBlock or TileID.IceBlock => new Color(170,210,225),
                    TileID.Ash or TileID.Hellstone => new Color(190,80,55),
                    _ => new Color(120,125,135)
                };
            sb.Draw(pixel, new Rectangle(area.X + gx * cell, area.Y + gy * cell, cell - 1, cell - 1), c);
        }
        sb.Draw(pixel, new Rectangle(area.X + cols / 2 * cell - 2, area.Y + rows / 2 * cell - 2, 5, 5), Color.Cyan);
        if (worldEdit)
        {
            if (worldPos1.HasValue) DrawMapMarker(sb, pixel, area, left, top, worldPos1.Value, span, cell, Color.LimeGreen);
            if (worldPos2.HasValue) DrawMapMarker(sb, pixel, area, left, top, worldPos2.Value, span, cell, Color.Orange);
        }
        if (!area.Contains(mouse) || !Clicked) return;
        int col = Math.Clamp((mouse.X - area.X) / cell, 0, cols - 1);
        int row = Math.Clamp((mouse.Y - area.Y) / cell, 0, rows - 1);
        int wx = Math.Clamp(left + col * span, 1, Main.maxTilesX - 2);
        int wy = Math.Clamp(top + row * span, 1, Main.maxTilesY - 2);
        Main.mouseLeftRelease = false;
        if (worldEdit)
        {
            if (worldEditSetPos1Next) { worldPos1 = new Point(wx, wy); worldEditSetPos1Next = false; SetStatus($"POS1: {wx}, {wy}"); }
            else { worldPos2 = new Point(wx, wy); worldEditSetPos1Next = true; SetStatus($"POS2: {wx}, {wy}"); }
        }
        else { teleportX = wx.ToString(); teleportY = wy.ToString(); SetStatus($"Destino seleccionado: {wx}, {wy}"); }
    }

    private static void DrawMapMarker(SpriteBatch sb, Texture2D pixel, Rectangle area, int left, int top, Point p, int span, int cell, Color color)
    {
        int x = area.X + (p.X - left) / span * cell, y = area.Y + (p.Y - top) / span * cell;
        if (x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom)
            sb.Draw(pixel, new Rectangle(x, y, cell, cell), color);
    }

    private static void TeleportToCoordinates()
    {
        if (!int.TryParse(teleportX, out int x) || !int.TryParse(teleportY, out int y)
            || x < 1 || x >= Main.maxTilesX - 1 || y < 1 || y >= Main.maxTilesY - 1)
        { SetStatus("Coordenadas inválidas."); return; }
        if (IsServer) SendCmd($"/tppos {x} {y}");
        else TeleportPlayer(x * 16, y * 16, $"Coordenadas {x}, {y}");
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
        y += RowH + 8;
        Utils.DrawBorderString(sb, "Destino X (tiles):", new Vector2(panel.X + Pad, y), Color.White, 0.7f);
        Utils.DrawBorderString(sb, "Destino Y (tiles):", new Vector2(panel.X + 230, y), Color.White, 0.7f);
        y += 17;
        if (string.IsNullOrEmpty(teleportX)) teleportX = ((int)(Main.LocalPlayer.Center.X / 16f)).ToString();
        if (string.IsNullOrEmpty(teleportY)) teleportY = ((int)(Main.LocalPlayer.Center.Y / 16f)).ToString();
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 150, 24), 26, teleportX, mouse, "X");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 150, 24), 27, teleportY, mouse, "Y");
        y += 29;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, Width - Pad * 2, RowH), "Teletransportar a X/Y", mouse, new Color(45, 100, 150)))
            TeleportToCoordinates();
        y += RowH + 6;
        Utils.DrawBorderString(sb, "Mini mapa · clic para elegir destino", new Vector2(panel.X + Pad, y), new Color(145, 200, 240), 0.68f);
        y += 18;
        DrawWorldMiniMap(sb, pixel, new Rectangle(panel.X + Pad, y, Width - Pad * 2, Math.Max(50, panel.Bottom - y - 35)), mouse, false);
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



    // ------------------------------------------------------------------ Editor local de objetos

    private static void DrawItemEditorTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Editor de objetos · inventario local");
        Utils.DrawBorderString(sb, "Ranura de mochila (1-50):", new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Cantidad:", new Vector2(panel.X + 230, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Daño:", new Vector2(panel.X + 390, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 95, 26), 6, editorSlot, mouse, "1");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 110, 26), 10, editorStack, mouse, "cantidad");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 390, y, 110, 26), 7, editorDamage, mouse, "daño");
        y += 34;

        Utils.DrawBorderString(sb, "Defensa:", new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "UseTime:", new Vector2(panel.X + 230, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Valor (cobre):", new Vector2(panel.X + 390, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 110, 26), 8, editorDefense, mouse, "defensa");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 110, 26), 9, editorUseTime, mouse, "useTime");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 390, y, 140, 26), 11, editorValue, mouse, "valor");
        y += 36;

        int bw = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Cargar datos de la ranura", mouse, new Color(45, 90, 130)))
            LoadItemEditorSlot();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Aplicar cambios", mouse, new Color(55, 115, 75)))
            ApplyItemEditorSlot();
        y += RowH + 8;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Duplicar objeto en hueco vacío", mouse))
            DuplicateItemEditorSlot();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Reparar / restablecer objeto", mouse))
            ResetItemEditorSlot();
        y += RowH + 10;

        Utils.DrawBorderString(sb, "ID del objeto:", new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Prefijo (0 = ninguno):", new Vector2(panel.X + 230, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 110, 26), 14, editorType, mouse, "ID");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 130, 26), 15, editorPrefix, mouse, "prefijo");
        y += 34;
        int bwExtra = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bwExtra, RowH), "Crear / reemplazar ranura", mouse, new Color(45, 105, 145)))
            CreateItemEditorSlot();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bwExtra + 8, y, bwExtra, RowH), "Vaciar ranura", mouse, new Color(135, 55, 55)))
            ClearItemEditorSlot();
        y += RowH + 8;
        Utils.DrawBorderString(sb, "Retroceso:", new Vector2(panel.X + Pad, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, "Crítico:", new Vector2(panel.X + 195, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, "Rareza:", new Vector2(panel.X + 365, y), Color.White, 0.68f);
        y += 17;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 125, 24), 20, editorKnockback, mouse, "knockback");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 195, y, 100, 24), 21, editorCrit, mouse, "crítico");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 365, y, 100, 24), 22, editorRarity, mouse, "rareza");
        y += 30;
        Utils.DrawBorderString(sb, "Animación:", new Vector2(panel.X + Pad, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, "Velocidad proyectil:", new Vector2(panel.X + 230, y), Color.White, 0.68f);
        Utils.DrawBorderString(sb, "ID proyectil (0 = ninguno):", new Vector2(panel.X + 400, y), Color.White, 0.62f);
        y += 17;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 125, 24), 23, editorUseAnimation, mouse, "useAnimation");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 140, 24), 24, editorShootSpeed, mouse, "shootSpeed");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 400, y, 120, 24), 25, editorProjectile, mouse, "ID proyectil");
        y += 30;

        var p = Main.LocalPlayer;
        int slot = ParseEditorSlot();
        if (slot >= 0 && slot < p.inventory.Length)
        {
            Item it = p.inventory[slot];
            if (it != null && !it.IsAir)
            {
                DrawInfoLine(sb, panel, ref y, "Objeto seleccionado", $"{it.Name} (ID {it.type})");
                DrawInfoLine(sb, panel, ref y, "Estadísticas actuales", $"Daño {it.damage} · Defensa {it.defense} · UseTime {it.useTime}");
                DrawInfoLine(sb, panel, ref y, "Cantidad / valor", $"{it.stack} / {it.value} monedas de cobre");
                DrawInfoLine(sb, panel, ref y, "Combate", $"Crítico {it.crit}% · Retroceso {it.knockBack:0.##} · Rareza {it.rare}");
                DrawInfoLine(sb, panel, ref y, "Uso / proyectil", $"Animación {it.useAnimation} · Velocidad {it.shootSpeed:0.##} · ID {it.shoot}");
            }
            else
            {
                Utils.DrawBorderString(sb, "La ranura seleccionada está vacía.", new Vector2(panel.X + Pad, y), Color.Gray, 0.72f);
                y += 20;
            }
        }

        Utils.DrawBorderString(sb, "Limitación: cambios locales; un servidor puede rechazarlos o sobrescribirlos.",
            new Vector2(panel.X + Pad, panel.Bottom - 45), new Color(235, 195, 120), 0.62f);
    }

    private static int ParseEditorSlot()
    {
        if (!int.TryParse(editorSlot, out int slot))
            return -1;
        return slot - 1;
    }

    private static void LoadItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length || p.inventory[slot] == null || p.inventory[slot].IsAir)
        {
            SetStatus("La ranura indicada no contiene un objeto válido.");
            return;
        }
        Item it = p.inventory[slot];
        editorDamage = it.damage.ToString();
        editorDefense = it.defense.ToString();
        editorUseTime = it.useTime.ToString();
        editorStack = it.stack.ToString();
        editorValue = it.value.ToString();
        editorType = it.type.ToString();
        editorPrefix = it.prefix.ToString();
        editorKnockback = it.knockBack.ToString(System.Globalization.CultureInfo.InvariantCulture);
        editorCrit = it.crit.ToString();
        editorUseAnimation = it.useAnimation.ToString();
        editorShootSpeed = it.shootSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        editorProjectile = it.shoot.ToString();
        editorRarity = it.rare.ToString();
        SetStatus($"Datos cargados: {it.Name}.");
    }

    private static void ApplyItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length || p.inventory[slot] == null || p.inventory[slot].IsAir)
        {
            SetStatus("Selecciona una ranura con un objeto.");
            return;
        }

        Item it = p.inventory[slot];
        if (int.TryParse(editorType, out int requestedType) && requestedType > 0 && requestedType < ItemID.Count && requestedType != it.type)
        {
            it.SetDefaults(requestedType);
            it.stack = 1;
        }
        if (int.TryParse(editorPrefix, out int requestedPrefix))
        {
            int safePrefix = Math.Clamp(requestedPrefix, 0, PrefixID.Count - 1);
            it.prefix = (byte)safePrefix;
        }
        if (int.TryParse(editorDamage, out int damage)) it.damage = Math.Clamp(damage, 0, 1000000);
        if (int.TryParse(editorDefense, out int defense)) it.defense = Math.Clamp(defense, 0, 100000);
        if (int.TryParse(editorUseTime, out int useTime)) it.useTime = Math.Clamp(useTime, 1, 10000);
        if (int.TryParse(editorStack, out int stack)) it.stack = Math.Clamp(stack, 1, Math.Max(1, it.maxStack));
        if (int.TryParse(editorValue, out int value)) it.value = Math.Clamp(value, 0, int.MaxValue);
        if (float.TryParse(editorKnockback, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float knockback))
            it.knockBack = Math.Clamp(knockback, 0f, 10000f);
        if (int.TryParse(editorCrit, out int crit)) it.crit = Math.Clamp(crit, 0, 1000);
        if (int.TryParse(editorUseAnimation, out int animation)) it.useAnimation = Math.Clamp(animation, 1, 10000);
        if (float.TryParse(editorShootSpeed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float shootSpeed))
            it.shootSpeed = Math.Clamp(shootSpeed, 0f, 10000f);
        if (int.TryParse(editorProjectile, out int projectileId))
        {
            if (projectileId >= 0 && projectileId < ProjectileID.Count)
                it.shoot = projectileId;
            else
                SetStatus($"ID de proyectil fuera de rango (0-{ProjectileID.Count - 1}).");
        }
        if (int.TryParse(editorRarity, out int rarity)) it.rare = (sbyte)Math.Clamp(rarity, -1, 11);
        if (Main.netMode == 0)
            SetStatus($"Editado localmente: {it.Name}. Algunas estadísticas pueden recalcularse por el juego.");
        else
            SetStatus("Cambios aplicados solo al cliente; el servidor puede sobrescribirlos.");
    }

    private static void DuplicateItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length || p.inventory[slot] == null || p.inventory[slot].IsAir)
        {
            SetStatus("Selecciona primero un objeto válido.");
            return;
        }
        for (int i = 0; i < p.inventory.Length; i++)
        {
            if (p.inventory[i] == null || p.inventory[i].IsAir)
            {
                p.inventory[i] = p.inventory[slot].Clone();
                SetStatus($"Copia local creada en la ranura {i + 1}.");
                return;
            }
        }
        SetStatus("No hay espacios vacíos en la mochila.");
    }

    private static void CreateItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length)
        {
            SetStatus("Ranura inválida. Usa un número entre 1 y 50.");
            return;
        }
        if (!int.TryParse(editorType, out int type) || type <= 0 || type >= ItemID.Count)
        {
            SetStatus("ID de objeto inválido.");
            return;
        }
        Item created = new Item();
        created.SetDefaults(type);
        created.stack = 1;
        if (int.TryParse(editorStack, out int amount))
            created.stack = Math.Clamp(amount, 1, Math.Max(1, created.maxStack));
        if (int.TryParse(editorPrefix, out int prefix))
        {
            prefix = Math.Clamp(prefix, 0, PrefixID.Count - 1);
            created.prefix = (byte)prefix;
        }
        p.inventory[slot] = created;
        editorDamage = created.damage.ToString();
        editorDefense = created.defense.ToString();
        editorUseTime = created.useTime.ToString();
        editorStack = created.stack.ToString();
        editorValue = created.value.ToString();
        SetStatus($"Objeto creado en ranura {slot + 1}: {created.Name}.");
    }

    private static void ClearItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length)
        {
            SetStatus("Ranura inválida.");
            return;
        }
        p.inventory[slot].TurnToAir();
        SetStatus($"Ranura {slot + 1} vaciada localmente.");
    }

    private static void ResetItemEditorSlot()
    {
        int slot = ParseEditorSlot();
        var p = Main.LocalPlayer;
        if (slot < 0 || slot >= p.inventory.Length || p.inventory[slot] == null || p.inventory[slot].IsAir)
        {
            SetStatus("Selecciona primero un objeto válido.");
            return;
        }
        int type = p.inventory[slot].type;
        int stack = p.inventory[slot].stack;
        p.inventory[slot].SetDefaults(type);
        p.inventory[slot].stack = Math.Clamp(stack, 1, Math.Max(1, p.inventory[slot].maxStack));
        LoadItemEditorSlot();
        SetStatus("Estadísticas restauradas a los valores base del objeto.");
    }


    // ------------------------------------------------------------------ Editor de vida y maná

    private static void DrawLifeEditorTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        var p = Main.LocalPlayer;
        DrawSectionHeader(sb, panel, ref y, "Editor de vida y maná");
        DrawInfoLine(sb, panel, ref y, "Estado actual", $"Vida {p.statLife}/{p.statLifeMax2}  ·  Maná {p.statMana}/{p.statManaMax2}");
        DrawInfoLine(sb, panel, ref y, "Límites base", $"Vida base {p.statLifeMax}  ·  Maná base {p.statManaMax}");
        y += 4;

        if (string.IsNullOrEmpty(lifeCurrent)) lifeCurrent = p.statLife.ToString();
        if (string.IsNullOrEmpty(lifeMaximum)) lifeMaximum = p.statLifeMax2.ToString();
        if (string.IsNullOrEmpty(manaCurrent)) manaCurrent = p.statMana.ToString();
        if (string.IsNullOrEmpty(manaMaximum)) manaMaximum = p.statManaMax2.ToString();

        Utils.DrawBorderString(sb, "Vida actual", new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Vida máxima", new Vector2(panel.X + 230, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 150, 26), 16, lifeCurrent, mouse, "1 - 999999");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 150, 26), 17, lifeMaximum, mouse, "máximo");
        y += 34;

        Utils.DrawBorderString(sb, "Maná actual", new Vector2(panel.X + Pad, y), Color.White, 0.72f);
        Utils.DrawBorderString(sb, "Maná máximo", new Vector2(panel.X + 230, y), Color.White, 0.72f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 150, 26), 18, manaCurrent, mouse, "0 - 999999");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 230, y, 150, 26), 19, manaMaximum, mouse, "máximo");
        y += 36;

        int bw = (Width - Pad * 2 - 8) / 2;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Aplicar vida y maná", mouse, new Color(45, 115, 75)))
            ApplyLifeEditor();
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Curar al máximo", mouse, new Color(45, 90, 135)))
        {
            p.statLife = p.statLifeMax2;
            p.statMana = p.statManaMax2;
            lifeCurrent = p.statLife.ToString();
            lifeMaximum = p.statLifeMax2.ToString();
            manaCurrent = p.statMana.ToString();
            manaMaximum = p.statManaMax2.ToString();
            SyncSelf();
            SetStatus("Vida y maná restaurados al máximo permitido.");
        }
        y += RowH + 8;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Vida al 50%", mouse))
        {
            p.statLife = Math.Max(1, p.statLifeMax2 / 2);
            lifeCurrent = p.statLife.ToString();
            SyncSelf();
            SetStatus("Vida ajustada al 50%.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Maná al 50%", mouse))
        {
            p.statMana = Math.Max(0, p.statManaMax2 / 2);
            manaCurrent = p.statMana.ToString();
            SyncSelf();
            SetStatus("Maná ajustado al 50%.");
        }
        y += RowH + 8;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Máximos estándar (500/200)", mouse))
        {
            p.statLifeMax = 500; p.statLifeMax2 = 500; p.statManaMax = 200; p.statManaMax2 = 200;
            p.statLife = Math.Min(p.statLife, 500); p.statMana = Math.Min(p.statMana, 200);
            lifeCurrent = p.statLife.ToString(); lifeMaximum = "500"; manaCurrent = p.statMana.ToString(); manaMaximum = "200";
            SyncSelf(); SetStatus("Máximos estándar aplicados localmente.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Máximos altos (1000/500)", mouse))
        {
            p.statLifeMax = 1000; p.statLifeMax2 = 1000; p.statManaMax = 500; p.statManaMax2 = 500;
            p.statLife = Math.Min(p.statLife, 1000); p.statMana = Math.Min(p.statMana, 500);
            lifeCurrent = p.statLife.ToString(); lifeMaximum = "1000"; manaCurrent = p.statMana.ToString(); manaMaximum = "500";
            SyncSelf(); SetStatus("Máximos altos aplicados localmente.");
        }
        y += RowH + 8;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, bw, RowH), "Vida crítica (1 HP)", mouse, new Color(145, 55, 55)))
        {
            p.statLife = 1; lifeCurrent = "1"; SyncSelf(); SetStatus("Vida fijada en 1 HP.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + bw + 8, y, bw, RowH), "Restablecer máximos base", mouse))
        {
            p.statLifeMax = 100; p.statLifeMax2 = 100; p.statManaMax = 20; p.statManaMax2 = 20;
            p.statLife = Math.Min(p.statLife, 100); p.statMana = Math.Min(p.statMana, 20);
            lifeCurrent = p.statLife.ToString(); lifeMaximum = "100"; manaCurrent = p.statMana.ToString(); manaMaximum = "20";
            SyncSelf(); SetStatus("Máximos base restablecidos localmente.");
        }
        y += RowH + 8;
        Utils.DrawBorderString(sb, "Nota: en multijugador el servidor puede corregir o rechazar cambios locales.",
            new Vector2(panel.X + Pad, y), new Color(235, 195, 120), 0.66f);
    }

    private static void ApplyLifeEditor()
    {
        var p = Main.LocalPlayer;
        if (!int.TryParse(lifeMaximum, out int maxLife) || maxLife < 1 || maxLife > 999999
            || !int.TryParse(lifeCurrent, out int currentLife) || currentLife < 0 || currentLife > maxLife
            || !int.TryParse(manaMaximum, out int maxMana) || maxMana < 0 || maxMana > 999999
            || !int.TryParse(manaCurrent, out int currentMana) || currentMana < 0 || currentMana > maxMana)
        {
            SetStatus("Valores inválidos: actual debe estar entre 0 y máximo; revisa los cuatro campos.");
            return;
        }

        // Cambia los máximos base del personaje local. En multijugador el servidor puede
        // ignorarlos o recalcularlos, ya que no es una API administrativa del servidor.
        p.statLifeMax = maxLife;
        p.statManaMax = maxMana;
        p.statLifeMax2 = maxLife;
        p.statManaMax2 = maxMana;
        p.statLife = Math.Clamp(currentLife, 0, maxLife);
        p.statMana = Math.Clamp(currentMana, 0, maxMana);
        lifeCurrent = p.statLife.ToString();
        lifeMaximum = p.statLifeMax2.ToString();
        manaCurrent = p.statMana.ToString();
        manaMaximum = p.statManaMax2.ToString();
        SyncSelf();
        SetStatus("Vida y maná ajustados localmente. En servidores, el servidor puede sobrescribirlos.");
    }


    // ------------------------------------------------------------------ WorldEdit basico local

    private static void DrawWorldEditTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "WorldEdit · selección y edición");
        int bx = panel.X + Pad;
        int bw = (Width - Pad * 2 - 8) / 2;
        string p1 = worldPos1.HasValue ? $"{worldPos1.Value.X}, {worldPos1.Value.Y}" : "Sin seleccionar";
        string p2 = worldPos2.HasValue ? $"{worldPos2.Value.X}, {worldPos2.Value.Y}" : "Sin seleccionar";
        DrawInfoLine(sb, panel, ref y, "POS1", p1);
        DrawInfoLine(sb, panel, ref y, "POS2", p2);
        if (DrawButton(sb, pixel, new Rectangle(bx, y, bw, RowH), "Marcar POS1 aquí", mouse, new Color(45, 90, 135)))
        {
            var p = Main.LocalPlayer;
            worldPos1 = new Point((int)(p.Center.X / 16f), (int)(p.Center.Y / 16f));
            SetStatus($"POS1: {worldPos1.Value.X}, {worldPos1.Value.Y}");
        }
        if (DrawButton(sb, pixel, new Rectangle(bx + bw + 8, y, bw, RowH), "Marcar POS2 aquí", mouse, new Color(45, 90, 135)))
        {
            var p = Main.LocalPlayer;
            worldPos2 = new Point((int)(p.Center.X / 16f), (int)(p.Center.Y / 16f));
            SetStatus($"POS2: {worldPos2.Value.X}, {worldPos2.Value.Y}");
        }
        y += RowH + 8;
        Utils.DrawBorderString(sb, "Tile ID (0 = aire/borrar):", new Vector2(bx, y), Color.White, 0.7f);
        Utils.DrawBorderString(sb, "Reemplazar tile ID:", new Vector2(bx + 250, y), Color.White, 0.7f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(bx, y, 190, 25), 12, worldTileType, mouse, "1");
        DrawTextField(sb, pixel, new Rectangle(bx + 250, y, 190, 25), 13, worldReplaceType, mouse, "0");
        y += 33;
        if (DrawButton(sb, pixel, new Rectangle(bx, y, bw, RowH), "Fill selección", mouse, new Color(50, 105, 70)))
            ApplyWorldEdit("fill");
        if (DrawButton(sb, pixel, new Rectangle(bx + bw + 8, y, bw, RowH), "Clear selección", mouse, new Color(135, 65, 60)))
            ApplyWorldEdit("clear");
        y += RowH + 7;
        if (DrawButton(sb, pixel, new Rectangle(bx, y, bw, RowH), "Replace ID → ID", mouse, new Color(80, 75, 130)))
            ApplyWorldEdit("replace");
        if (DrawButton(sb, pixel, new Rectangle(bx + bw + 8, y, bw, RowH), "Undo última acción", mouse))
            UndoWorldEdit();
        y += RowH + 7;
        Utils.DrawBorderString(sb, "Mini mapa: clic alterna POS1 y POS2.", new Vector2(bx, y), new Color(145, 200, 240), 0.68f);
        y += 18;
        DrawWorldMiniMap(sb, pixel, new Rectangle(bx, y, Width - Pad * 2, Math.Max(48, panel.Bottom - y - 45)), mouse, true);
        Utils.DrawBorderString(sb, "WorldEdit del cliente sin bloqueo por modo. En multijugador, el servidor puede descartar cambios locales.",
            new Vector2(bx, panel.Bottom - 45), new Color(235, 195, 120), 0.62f);
    }

    private static void ApplyWorldEdit(string operation)
    {
        if (!worldPos1.HasValue || !worldPos2.HasValue)
        {
            SetStatus("Marca POS1 y POS2 antes de editar.");
            return;
        }
        int x1 = Math.Clamp(Math.Min(worldPos1.Value.X, worldPos2.Value.X), 1, Main.maxTilesX - 2);
        int x2 = Math.Clamp(Math.Max(worldPos1.Value.X, worldPos2.Value.X), 1, Main.maxTilesX - 2);
        int y1 = Math.Clamp(Math.Min(worldPos1.Value.Y, worldPos2.Value.Y), 1, Main.maxTilesY - 2);
        int y2 = Math.Clamp(Math.Max(worldPos1.Value.Y, worldPos2.Value.Y), 1, Main.maxTilesY - 2);
        int tileId = int.TryParse(worldTileType, out int parsedTile) ? parsedTile : 1;
        int replaceId = int.TryParse(worldReplaceType, out int parsedReplace) ? parsedReplace : 0;
        if (tileId < 0 || tileId >= TileID.Count || replaceId < 0 || replaceId >= TileID.Count)
        {
            SetStatus("Tile ID fuera de rango.");
            return;
        }

        worldEditUndo.Clear();
        int changed = 0;
        for (int x = x1; x <= x2; x++)
        for (int yy = y1; yy <= y2; yy++)
        {
            Tile tile = Main.tile[x, yy];
            if (tile == null) continue;
            if (operation == "replace" && (!tile.HasTile || tile.TileType != replaceId))
                continue;
            if (operation == "fill" && tileId == 0)
                operation = "clear";
            worldEditUndo.Add((x, yy, (Tile)tile.Clone()));
            if (operation == "clear")
                WorldGen.KillTile(x, yy, false, false, true);
            else if (operation == "fill")
                WorldGen.PlaceTile(x, yy, tileId, true, true);
            else if (operation == "replace")
            {
                WorldGen.KillTile(x, yy, false, false, true);
                if (tileId > 0) WorldGen.PlaceTile(x, yy, tileId, true, true);
            }
            changed++;
        }
        worldEditUndoType = operation == "clear" ? 0 : tileId;
        SetStatus($"WorldEdit {operation}: {changed} tiles. Undo disponible para la última operación.");
    }

    private static void UndoWorldEdit()
    {
        if (worldEditUndo.Count == 0)
        {
            SetStatus("No hay una operación para deshacer.");
            return;
        }
        foreach (var entry in worldEditUndo)
            Main.tile[entry.x, entry.y].CopyFrom(entry.tile);
        int count = worldEditUndo.Count;
        worldEditUndo.Clear();
        SetStatus($"Undo completado: restaurados {count} tiles.");
    }

    // ------------------------------------------------------------------ Herramientas adicionales: 200 acciones categorizadas

    private static void DrawToolsTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Centro de herramientas · 200 acciones por categorías");
        Utils.DrawBorderString(sb, "Objetivo (jugador/warp/región/clave):", new Vector2(panel.X + Pad, y), Color.White, 0.66f);
        Utils.DrawBorderString(sb, "Motivo (kick/ban):", new Vector2(panel.X + 330, y), Color.White, 0.66f);
        y += 18;
        DrawTextField(sb, pixel, new Rectangle(panel.X + Pad, y, 300, 25), 4, moderationTarget, mouse, "Nombre exacto o argumento...");
        DrawTextField(sb, pixel, new Rectangle(panel.X + 320, y, 300, 25), 5, moderationReason, mouse, "Motivo opcional...");
        y += 32;

        int pageSize = 12;
        int first = toolsPage * pageSize;
        int cols = 2;
        int buttonW = (Width - Pad * 2 - 8) / cols;
        for (int localIndex = 0; localIndex < pageSize; localIndex++)
        {
            int index = first + localIndex;
            if (index >= extraTools.Length)
                break;

            int col = localIndex % cols;
            int row = localIndex / cols;
            var rect = new Rectangle(panel.X + Pad + col * (buttonW + 8), y + row * 29, buttonW, 25);
            string toolLabel = extraTools[index].label;
            Color color = toolLabel.StartsWith("MODERACIÓN") || toolLabel.StartsWith("USUARIOS") || toolLabel.StartsWith("REGIONES") ? new Color(112, 55, 62)
                : toolLabel.StartsWith("TIEMPO") || toolLabel.StartsWith("CLIMA") || toolLabel.StartsWith("EVENTOS") || toolLabel.StartsWith("PROGRESIÓN") ? new Color(45, 86, 120)
                : toolLabel.StartsWith("TELETRANSPORTE") || toolLabel.StartsWith("JUGADOR") ? new Color(48, 91, 75)
                : toolLabel.StartsWith("OBJETOS") || toolLabel.StartsWith("NPC") ? new Color(92, 76, 43)
                : toolLabel.StartsWith("SERVIDOR") || toolLabel.StartsWith("CONFIGURACIÓN") ? new Color(75, 75, 88)
                : toolLabel.StartsWith("ECONOMÍA") ? new Color(92, 58, 112)
                : new Color(50, 55, 68);
            if (DrawButton(sb, pixel, rect, $"{index + 1:000}. {extraTools[index].label}", mouse, color))
                RunExtraTool(index);
        }

        int navY = panel.Bottom - 73;
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, navY, 130, 23), "Anterior", mouse))
            toolsPage = Math.Max(0, toolsPage - 1);
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 138, navY, 130, 23), "Siguiente", mouse))
            toolsPage = Math.Min((extraTools.Length - 1) / pageSize, toolsPage + 1);
        Utils.DrawBorderString(sb, $"Página {toolsPage + 1}/{(extraTools.Length + pageSize - 1) / pageSize} · {extraTools.Length} acciones",
            new Vector2(panel.X + Pad + 285, navY + 4), new Color(180, 205, 235), 0.72f);
        Utils.DrawBorderString(sb, "Solo se envían comandos; el servidor y sus permisos deciden si se ejecutan.",
            new Vector2(panel.X + Pad, panel.Bottom - 43), new Color(235, 195, 120), 0.61f);
    }

    private static void RunExtraTool(int index)
    {
        if (index < 0 || index >= extraTools.Length)
            return;

        if (Main.netMode != 1 || serverMode != ServerMode.Comandos)
        {
            SetStatus("Estas herramientas requieren multijugador y modo COMANDOS (TShock/plugins).");
            return;
        }

        string template = extraTools[index].command;
        if (template.Contains("{target}") && string.IsNullOrWhiteSpace(moderationTarget))
        {
            SetStatus("Escribe primero el objetivo en el campo superior.");
            return;
        }

        string target = Quote(moderationTarget.Trim());
        string reason = string.IsNullOrWhiteSpace(moderationReason) ? "" : Quote(moderationReason.Trim());
        string command = template.Replace("{target}", target).Replace("{reason}", reason);
        while (command.Contains("  "))
            command = command.Replace("  ", " ");
        command = command.Trim();

        SendCmd(command);
        SetStatus($"Herramienta enviada: {extraTools[index].label}. El servidor debe permitir el comando.");
    }

    // ------------------------------------------------------------------ Personalización de la interfaz
    private static void DrawCustomizeTab(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
    {
        DrawSectionHeader(sb, panel, ref y, "Personalizar interfaz");
        Utils.DrawBorderString(sb, "Cambia los colores del panel sin desactivar ni reemplazar ninguna función.",
            new Vector2(panel.X + Pad, y), Color.LightGray, 0.68f);
        y += 28;

        string[] themeNames = { "Azul", "Verde", "Violeta", "Naranja", "Rojo" };
        int gap = 7;
        int bw = (Width - Pad * 2 - gap * 4) / 5;
        for (int i = 0; i < themeNames.Length; i++)
        {
            var rect = new Rectangle(panel.X + Pad + i * (bw + gap), y, bw, 32);
            if (DrawButton(sb, pixel, rect, themeNames[i], mouse, UiAccents[i]))
            {
                uiTheme = i;
                SetStatus($"Tema aplicado: {themeNames[i]}.");
            }
            if (uiTheme == i)
                sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 3, rect.Width, 3), Color.White);
        }
        y += 44;

        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad, y, 245, RowH + 4),
            uiHighContrast ? "Contraste alto: ACTIVADO" : "Contraste alto: desactivado", mouse,
            uiHighContrast ? UiAccent : null))
        {
            uiHighContrast = !uiHighContrast;
            SetStatus(uiHighContrast ? "Contraste alto activado." : "Contraste alto desactivado.");
        }
        if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 253, y, 245, RowH + 4),
            "Restablecer apariencia", mouse))
        {
            uiTheme = 0;
            uiHighContrast = false;
            SetStatus("Apariencia predeterminada restaurada.");
        }
        y += RowH + 18;

        DrawSectionHeader(sb, panel, ref y, "Vista previa");
        var preview = new Rectangle(panel.X + Pad, y, Width - Pad * 2, 76);
        sb.Draw(pixel, preview, UiPanelColor);
        sb.Draw(pixel, new Rectangle(preview.X, preview.Y, preview.Width, 23), UiTitleColor);
        Utils.DrawBorderString(sb, "HERO's Mod · Vista previa", new Vector2(preview.X + 9, preview.Y + 4), Color.White, 0.75f);
        sb.Draw(pixel, new Rectangle(preview.X + 9, preview.Y + 34, 135, 27), UiAccent);
        Utils.DrawBorderString(sb, "Botón destacado", new Vector2(preview.X + 18, preview.Y + 39), Color.White, 0.68f);
        sb.Draw(pixel, new Rectangle(preview.X + 153, preview.Y + 34, 135, 27), UiTabColor);
        Utils.DrawBorderString(sb, "Botón normal", new Vector2(preview.X + 162, preview.Y + 39), Color.White, 0.68f);
        y += 91;

        Utils.DrawBorderString(sb, "Los cambios son visuales y duran mientras siga abierta esta sesión del juego.",
            new Vector2(panel.X + Pad, y), new Color(235, 195, 120), 0.66f);
        y += 20;
        Utils.DrawBorderString(sb, "Consejo: arrastra la barra superior para mover el panel y usa F8 para abrir/cerrar.",
            new Vector2(panel.X + Pad, y), Color.LightGray, 0.66f);
    }

    // ------------------------------------------------------------------ dibujo comun

    private static void DrawSectionHeader(SpriteBatch sb, Rectangle panel, ref int y, string title)
    {
        Utils.DrawBorderString(sb, title, new Vector2(panel.X + Pad, y), UiAccent, 0.82f);
        y += 22;
    }

    private static Color UiAccent => UiAccents[Math.Clamp(uiTheme, 0, UiAccents.Length - 1)];
    private static Color UiPanelColor => uiHighContrast ? new Color(12, 14, 20) : new Color(30, 33, 40);
    private static Color UiTitleColor => uiHighContrast ? new Color(25, 30, 42) : new Color(50, 56, 70);
    private static Color UiTabColor => uiHighContrast ? new Color(25, 29, 39) : new Color(45, 50, 60);

    private static bool DrawButton(SpriteBatch sb, Texture2D pixel, Rectangle rect, string text, Point mouse, Color? customBg = null)
    {
        bool hover = rect.Contains(mouse);
        Color bg = hover ? Color.Lerp(UiAccent, Color.White, 0.18f) : (customBg ?? new Color(50, 55, 68));
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
        else if (activeField == 4)
            moderationTarget = Main.GetInputText(moderationTarget);
        else if (activeField == 5)
            moderationReason = Main.GetInputText(moderationReason);
        else if (activeField == 6)
            editorSlot = Main.GetInputText(editorSlot);
        else if (activeField == 7)
            editorDamage = Main.GetInputText(editorDamage);
        else if (activeField == 8)
            editorDefense = Main.GetInputText(editorDefense);
        else if (activeField == 9)
            editorUseTime = Main.GetInputText(editorUseTime);
        else if (activeField == 10)
            editorStack = Main.GetInputText(editorStack);
        else if (activeField == 11)
            editorValue = Main.GetInputText(editorValue);
        else if (activeField == 12)
            worldTileType = Main.GetInputText(worldTileType);
        else if (activeField == 13)
            worldReplaceType = Main.GetInputText(worldReplaceType);
        else if (activeField == 14)
            editorType = Main.GetInputText(editorType);
        else if (activeField == 15)
            editorPrefix = Main.GetInputText(editorPrefix);
        else if (activeField == 16)
            lifeCurrent = Main.GetInputText(lifeCurrent);
        else if (activeField == 17)
            lifeMaximum = Main.GetInputText(lifeMaximum);
        else if (activeField == 18)
            manaCurrent = Main.GetInputText(manaCurrent);
        else if (activeField == 19)
            manaMaximum = Main.GetInputText(manaMaximum);
        else if (activeField == 20)
            editorKnockback = Main.GetInputText(editorKnockback);
        else if (activeField == 21)
            editorCrit = Main.GetInputText(editorCrit);
        else if (activeField == 22)
            editorRarity = Main.GetInputText(editorRarity);
        else if (activeField == 23)
            editorUseAnimation = Main.GetInputText(editorUseAnimation);
        else if (activeField == 24)
            editorShootSpeed = Main.GetInputText(editorShootSpeed);
        else if (activeField == 25)
            editorProjectile = Main.GetInputText(editorProjectile);
        else if (activeField == 26)
            teleportX = Main.GetInputText(teleportX);
        else if (activeField == 27)
            teleportY = Main.GetInputText(teleportY);
        else if (activeField == 31)
            profileNameInput = Main.GetInputText(profileNameInput);
        else if (activeField == 32)
            bookmarkXInput = Main.GetInputText(bookmarkXInput);
        else if (activeField == 33)
            bookmarkYInput = Main.GetInputText(bookmarkYInput);
        else if (activeField == 34)
            bookmarkNameInput = Main.GetInputText(bookmarkNameInput);

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
