using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Events;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI.Chat;

namespace Terraria.Unified.Features;

/// <summary>
/// Panel de administración / trucos dentro del juego (F8), con pestañas.
/// - Un jugador (netMode 0): todo se ejecuta en local, sin restricciones.
/// - Multijugador (netMode 1): solo los botones de Mundo/Jugador/Items mandan comandos de chat a
///   TShock, que sigue decidiendo los permisos. Las demás herramientas son solo de un jugador.
/// </summary>
internal static class AdminPanel
{
	private enum Tab { Mundo, Jugador, Items, Jefes, Eventos, Tele, Todo }

	private const int Width = 380;
	private const int Height = 440;
	private const int Pad = 8;
	private const int RowH = 24;
	private const int TitleH = 26;
	private const int TabH = 24;

	private static readonly Color Bg = new(20, 24, 48);
	private static readonly Color Header = new(60, 70, 140);
	private static readonly Color BtnNormal = new(55, 65, 130);
	private static readonly Color BtnHover = new(90, 105, 200);
	private static readonly Color BtnOn = new(40, 130, 80);
	private static readonly Color SectionColor = new(190, 200, 255);

	public static bool Visible { get; set; }

	private static Tab tab = Tab.Mundo;
	private static Vector2 position = new(20f, 120f);
	private static bool dragging;
	private static Vector2 dragOffset;

	// Campos: 1 item, 2 cantidad, 3 NPC (id/nombre), 4 X tile, 5 Y tile.
	private static int editing;
	private static readonly string[] fields = ["", "1", "", "", "", ""];

	// Toggles persistentes (un jugador).
	private static bool godMode, flying, light, freezeTime, bossesDowned;
	private static readonly float[] flySpeeds = [4f, 8f, 16f, 32f];
	private static int flySpeedIndex = 1;
	private static double frozenTime;
	private static bool frozenDay;

	private static string status = "";
	private static int statusTimer;

	private static bool Clicked => Main.mouseLeft && Main.mouseLeftRelease;
	private static bool Single => Main.netMode == 0;

	// ---------------- Bucle principal ----------------

	public static void Draw(SpriteBatch sb)
	{
		if (Main.dedServ || Main.gameMenu)
			return;

		ApplyToggles();
		HandleToggleKey();

		if (!Visible)
			return;

		var pixel = TextureAssets.MagicPixel.Value;
		var mouse = new Point(Main.mouseX, Main.mouseY);
		var panel = new Rectangle((int)position.X, (int)position.Y, Width, Height);

		if (panel.Contains(mouse))
			Main.LocalPlayer.mouseInterface = true;

		var titleBar = new Rectangle(panel.X, panel.Y, panel.Width, TitleH);
		if (dragging) {
			if (Main.mouseLeft) {
				position = new Vector2(mouse.X, mouse.Y) - dragOffset;
				position.X = MathHelper.Clamp(position.X, 0, Main.screenWidth - Width);
				position.Y = MathHelper.Clamp(position.Y, 0, Main.screenHeight - 40);
			}
			else {
				dragging = false;
			}
		}
		else if (Clicked && titleBar.Contains(mouse)) {
			dragging = true;
			dragOffset = new Vector2(mouse.X, mouse.Y) - position;
			Main.mouseLeftRelease = false;
		}

		sb.Draw(pixel, panel, Bg * 0.93f);
		sb.Draw(pixel, titleBar, Header * 0.95f);
		string mode = Main.netMode == 1 ? "Multijugador" : "Un jugador";
		Utils.DrawBorderString(sb, $"Panel Admin  [{mode}]  F8", new Vector2(panel.X + Pad, panel.Y + 4), Color.White, 0.85f);
		if (DrawButton(sb, pixel, new Rectangle(panel.Right - 24, panel.Y + 3, 20, 20), "x", mouse))
			Visible = false;

		var tabs = Enum.GetValues<Tab>();
		int tabW = (Width - Pad * 2) / tabs.Length;
		for (int i = 0; i < tabs.Length; i++) {
			var r = new Rectangle(panel.X + Pad + i * tabW, panel.Y + TitleH + 4, tabW - 2, TabH);
			if (DrawButton(sb, pixel, r, tabs[i].ToString(), mouse, tab == tabs[i] ? BtnOn : null) && tab != tabs[i]) {
				tab = tabs[i];
				StopEditing();
			}
		}

		int y = panel.Y + TitleH + TabH + 14;

		switch (tab) {
			case Tab.Mundo: DrawWorld(sb, pixel, panel, ref y, mouse); break;
			case Tab.Jugador: DrawPlayer(sb, pixel, panel, ref y, mouse); break;
			case Tab.Items: DrawItems(sb, pixel, panel, ref y, mouse); break;
			case Tab.Jefes: DrawBosses(sb, pixel, panel, ref y, mouse); break;
			case Tab.Eventos: DrawEvents(sb, pixel, panel, ref y, mouse); break;
			case Tab.Tele: DrawTeleport(sb, pixel, panel, ref y, mouse); break;
			case Tab.Todo: DrawUnlock(sb, pixel, panel, ref y, mouse); break;
		}

		if (statusTimer > 0) {
			statusTimer--;
			Utils.DrawBorderString(sb, status, new Vector2(panel.X + Pad, panel.Bottom - 22), new Color(255, 230, 140), 0.75f);
		}

		if (Clicked && editing != 0)
			StopEditing();

		if (editing != 0)
			UpdateTyping();
	}

	/// <summary>Efectos que se aplican cada frame aunque el panel esté cerrado.</summary>
	private static void ApplyToggles()
	{
		if (!Single)
			return;

		var p = Main.LocalPlayer;

		if (godMode) {
			p.statLife = p.statLifeMax2;
			p.statMana = p.statManaMax2;
			p.breath = p.breathMax;
			for (int i = p.buffType.Length - 1; i >= 0; i--) {
				int type = p.buffType[i];
				if (type > 0 && Main.debuff[type])
					p.DelBuff(i);
			}
		}

		if (light)
			Lighting.AddLight(p.Center, 1.6f, 1.6f, 1.6f);

		if (freezeTime) {
			Main.time = frozenTime;
			Main.dayTime = frozenDay;
		}

		if (flying && editing == 0 && !Main.drawingPlayerChat) {
			var k = Main.keyState;
			var dir = Vector2.Zero;
			if (k.IsKeyDown(Keys.W)) dir.Y -= 1f;
			if (k.IsKeyDown(Keys.S)) dir.Y += 1f;
			if (k.IsKeyDown(Keys.A)) dir.X -= 1f;
			if (k.IsKeyDown(Keys.D)) dir.X += 1f;

			if (dir != Vector2.Zero) {
				dir.Normalize();
				p.velocity = dir * flySpeeds[flySpeedIndex];
			}
			else {
				p.velocity = new Vector2(0f, -p.gravity);
			}

			p.fallStart = (int)(p.position.Y / 16f);
			p.fallStart2 = p.fallStart;
		}
	}

	// ---------------- Pestañas ----------------

	private static void DrawWorld(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Tiempo");
		Buttons(sb, pixel, panel, ref y, mouse, 4,
			B("Dia", () => Act("/time day", () => SetTime(true, 0))),
			B("Mediodia", () => Act("/time noon", () => SetTime(true, 27000))),
			B("Noche", () => Act("/time night", () => SetTime(false, 0))),
			B("Medianoche", () => Act("/time midnight", () => SetTime(false, 16200))),
			B("Congelar hora", () => Local(() => {
				freezeTime = !freezeTime;
				frozenTime = Main.time;
				frozenDay = Main.dayTime;
				Say(freezeTime ? "Hora congelada." : "Hora libre.");
			}), freezeTime),
			B("Fase luna +", () => Local(() => { Main.moonPhase = (Main.moonPhase + 1) % 8; Say($"Fase de luna {Main.moonPhase}."); })));

		Section(sb, panel, ref y, "Clima");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Lluvia ON", () => Act("/rain start", () => Main.StartRain())),
			B("Lluvia OFF", () => Act("/rain stop", () => Main.StopRain())),
			B("Tormenta arena", () => Local(() => CallStatic(typeof(Sandstorm), "StartSandstorm"))));

		Section(sb, panel, ref y, "Cielo");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Luna de sangre", () => Local(() => { Main.dayTime = false; Main.time = 0; Main.bloodMoon = true; Say("Luna de sangre."); })),
			B("Eclipse", () => Local(() => { Main.dayTime = true; Main.time = 0; Main.eclipse = true; Say("Eclipse solar."); })),
			B("Fiesta", () => Local(() => CallStatic(typeof(BirthdayParty), "ToggleManualParty"))));

		Section(sb, panel, ref y, "Mundo");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Matar enemigos", () => Act("/butcher", Butcher)),
			B("Meteorito", () => Local(() => WorldGen.dropMeteor())),
			B("Hardmode", () => Local(() => { WorldGen.StartHardmode(); Say("Hardmode activado."); })));
	}

	private static void DrawPlayer(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Jugador");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B(godMode && Single ? "Godmode ON" : "Godmode", () => Act("/godmode", () => {
				godMode = !godMode;
				Say(godMode ? "Godmode: vida, mana, aire e inmunidad a debuffs." : "Godmode desactivado.");
			}), godMode && Single),
			B("Curar", () => Act("/heal", Heal)),
			B("Vida/Mana max", () => Local(() => {
				var p = Main.LocalPlayer;
				p.statLifeMax = 500;
				p.statManaMax = 200;
				Heal();
			})),
			B(flying ? "Volar ON" : "Volar (WASD)", () => Local(() => {
				flying = !flying;
				Say(flying ? "Vuelo: WASD para moverte." : "Vuelo desactivado.");
			}), flying),
			B($"Vel vuelo: {flySpeeds[flySpeedIndex]:0}", () => Local(() => flySpeedIndex = (flySpeedIndex + 1) % flySpeeds.Length)),
			B(light ? "Luz ON" : "Luz", () => Local(() => { light = !light; Say(light ? "Luz activada." : "Luz desactivada."); }), light));

		Section(sb, panel, ref y, "Nota");
		Utils.DrawBorderString(sb, Single
			? "Un jugador: sin restricciones."
			: "Servidor: los permisos los decide TShock.",
			new Vector2(panel.X + Pad, y), Color.Gray, 0.75f);
	}

	private static void DrawItems(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Dar item (ID o nombre)");
		Field(sb, pixel, new Rectangle(panel.X + Pad, y, Width - Pad * 2, RowH), 1, "item...", mouse);
		y += RowH + 4;

		Field(sb, pixel, new Rectangle(panel.X + Pad, y, 70, RowH), 2, "cant", mouse);
		if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 78, y, Width - Pad * 2 - 78, RowH), "Dar item", mouse))
			GiveItem();
		y += RowH + 8;

		Section(sb, panel, ref y, "Rapidos");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Zenith", () => QuickGive("Zenith", 1)),
			B("Terra Blade", () => QuickGive("TerraBlade", 1)),
			B("Drax", () => QuickGive("Drax", 1)),
			B("Rod of Discord", () => QuickGive("RodofDiscord", 1)),
			B("Celular", () => QuickGive("CellPhone", 1)),
			B("Alas angel", () => QuickGive("AngelWings", 1)),
			B("Pocion vida x30", () => QuickGive("GreaterHealingPotion", 30)),
			B("Platino x20", () => QuickGive("PlatinumCoin", 20)),
			B("Antorchas x999", () => QuickGive("Torch", 999)));
	}

	private static void DrawBosses(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Invocar jefe (solo un jugador)");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			Boss("Rey Slime", 50), Boss("Ojo de Cthulhu", 4), Boss("Devoramundos", 13),
			Boss("Cerebro Cthulhu", 266), Boss("Reina Abeja", 222), Boss("Skeletron", 35),
			Boss("Deerclops", 668), Boss("Reina Slime", 657), Boss("Destructor", 134),
			Boss("Retinazer", 125), Boss("Spazmatism", 126), Boss("Skeletron Prime", 127),
			Boss("Plantera", 262), Boss("Golem", 245), Boss("Duke Fishron", 370),
			Boss("Emperatriz Luz", 636), Boss("Cultista", 439), Boss("Moon Lord", 398));

		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Muro de Carne", () => Local(() => {
				NPC.SpawnWOF(Main.LocalPlayer.Center);
				Say("Muro de Carne: debes estar en el Inframundo (pestaña Tele).");
			})));

		Section(sb, panel, ref y, "NPC por ID o nombre");
		Field(sb, pixel, new Rectangle(panel.X + Pad, y, 150, RowH), 3, "id o nombre", mouse);
		if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 158, y, Width - Pad * 2 - 158, RowH), "Invocar", mouse))
			Local(SpawnNpcFromField);
	}

	private static void DrawEvents(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Invasiones (solo un jugador)");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Goblins", () => Local(() => Main.StartInvasion(1))),
			B("Legion helada", () => Local(() => Main.StartInvasion(2))),
			B("Piratas", () => Local(() => Main.StartInvasion(3))),
			B("Marcianos", () => Local(() => Main.StartInvasion(4))),
			B("Detener invasion", () => Local(() => { Main.invasionType = 0; Main.invasionSize = 0; Say("Invasion detenida."); })),
			B("Matar enemigos", () => Act("/butcher", Butcher)));
	}

	private static void DrawTeleport(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Teletransporte (solo un jugador)");
		Buttons(sb, pixel, panel, ref y, mouse, 3,
			B("Spawn", () => Act("/spawn", () => TeleTile(Main.spawnTileX, Main.spawnTileY, "spawn"))),
			B("Cama", () => Local(() => {
				var p = Main.LocalPlayer;
				if (p.SpawnX >= 0 && p.SpawnY >= 0) TeleTile(p.SpawnX, p.SpawnY, "cama");
				else TeleTile(Main.spawnTileX, Main.spawnTileY, "spawn (sin cama)");
			})),
			B("Mazmorra", () => Local(() => TeleTile(Main.dungeonX, Main.dungeonY, "mazmorra"))),
			B("Inframundo", () => Local(() => TeleTile(Main.LocalPlayer.Center.ToTileCoordinates().X, Main.maxTilesY - 120, "Inframundo"))),
			B("Centro mundo", () => Local(() => TeleTile(Main.maxTilesX / 2, Main.spawnTileY, "centro"))),
			B("Mi posicion", () => Local(() => {
				var t = Main.LocalPlayer.Center.ToTileCoordinates();
				Say($"X={t.X}  Y={t.Y}");
			})));

		Section(sb, panel, ref y, "Ir a coordenadas (tiles)");
		Field(sb, pixel, new Rectangle(panel.X + Pad, y, 80, RowH), 4, "X", mouse);
		Field(sb, pixel, new Rectangle(panel.X + Pad + 88, y, 80, RowH), 5, "Y", mouse);
		if (DrawButton(sb, pixel, new Rectangle(panel.X + Pad + 176, y, Width - Pad * 2 - 176, RowH), "Ir", mouse)) {
			Local(() => {
				if (int.TryParse(fields[4].Trim(), out var x) && int.TryParse(fields[5].Trim(), out var yy))
					TeleTile(x, yy, "coordenadas");
				else
					Say("Escribe X e Y (numeros).");
			});
		}
	}

	private static void DrawUnlock(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse)
	{
		Section(sb, panel, ref y, "Desbloquear (solo un jugador)");
		Buttons(sb, pixel, panel, ref y, mouse, 2,
			B("Revelar mapa", () => Local(RevealMap)),
			B(bossesDowned ? "Jefes: derrotados" : "Marcar jefes derrotados", () => Local(() => {
				bossesDowned = !bossesDowned;
				SetDowned(bossesDowned);
				Say(bossesDowned ? "Todos los jefes marcados como derrotados." : "Jefes desmarcados.");
			}), bossesDowned),
			B("Quitar debuffs", () => Local(ClearDebuffs)),
			B("Hardmode", () => Local(() => { WorldGen.StartHardmode(); Say("Hardmode activado."); })),
			B("Todo ON (god+volar+luz)", () => Local(() => {
				godMode = flying = light = true;
				Say("God, vuelo y luz activados.");
			})),
			B("Todo OFF", () => Local(() => {
				godMode = flying = light = freezeTime = false;
				Say("Toggles desactivados.");
			})));

		Section(sb, panel, ref y, "Nota");
		Utils.DrawBorderString(sb, "Marcar jefes afecta tiendas, drops y eventos del mundo.", new Vector2(panel.X + Pad, y), Color.Gray, 0.7f);
	}

	// ---------------- Acciones ----------------

	private static (string Label, Action Do, bool On) B(string label, Action action, bool on = false) => (label, action, on);

	private static (string Label, Action Do, bool On) Boss(string name, int npcType) =>
		B(name, () => Local(() => {
			NPC.SpawnOnPlayer(Main.myPlayer, npcType);
			Say($"{name} invocado.");
		}));

	/// <summary>Ejecuta solo en un jugador.</summary>
	private static void Local(Action action)
	{
		if (Single)
			action();
		else
			Say("Esta herramienta es solo para un jugador.");
	}

	/// <summary>Multijugador: manda el comando. Un jugador: ejecuta la accion local.</summary>
	private static void Act(string command, Action local)
	{
		if (Main.netMode == 1)
			Run(command);
		else if (Single)
			local();
	}

	/// <summary>Llama a un metodo estatico aunque sea privado/internal.</summary>
	private static void CallStatic(Type type, string name)
	{
		var m = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (m == null) {
			Say($"No existe {type.Name}.{name} en esta version.");
			return;
		}
		m.Invoke(null, null);
		Say($"{type.Name}: {name}");
	}

	private static void SetTime(bool day, double time)
	{
		Main.dayTime = day;
		Main.time = time;
		if (freezeTime) {
			frozenDay = day;
			frozenTime = time;
		}
		Say("Hora cambiada.");
	}

	private static void Butcher()
	{
		int count = 0;
		foreach (var npc in Main.npc) {
			if (npc.active && !npc.friendly && !npc.townNPC && npc.lifeMax > 5) {
				npc.life = 0;
				npc.HitEffect();
				npc.active = false;
				count++;
			}
		}
		Say($"{count} enemigos eliminados.");
	}

	private static void Heal()
	{
		var p = Main.LocalPlayer;
		p.statLife = p.statLifeMax2;
		p.statMana = p.statManaMax2;
		Say("Vida y mana al maximo.");
	}

	private static void ClearDebuffs()
	{
		var p = Main.LocalPlayer;
		for (int i = p.buffType.Length - 1; i >= 0; i--) {
			int type = p.buffType[i];
			if (type > 0 && Main.debuff[type])
				p.DelBuff(i);
		}
		Say("Debuffs eliminados.");
	}

	private static void TeleTile(int tileX, int tileY, string where)
	{
		var p = Main.LocalPlayer;
		p.Teleport(new Vector2(tileX * 16f, tileY * 16f - p.height), 1);
		Say($"Teletransportado: {where}.");
	}

	private static void RevealMap()
	{
		for (int x = 0; x < Main.maxTilesX; x++) {
			for (int yy = 0; yy < Main.maxTilesY; yy++)
				Main.Map.Update(x, yy, 255);
		}
		Main.refreshMap = true;
		Say("Mapa revelado.");
	}

	private static void SetDowned(bool v)
	{
		NPC.downedSlimeKing = v;
		NPC.downedBoss1 = v;
		NPC.downedBoss2 = v;
		NPC.downedBoss3 = v;
		NPC.downedQueenBee = v;
		NPC.downedDeerclops = v;
		NPC.downedQueenSlime = v;
		NPC.downedMechBoss1 = v;
		NPC.downedMechBoss2 = v;
		NPC.downedMechBoss3 = v;
		NPC.downedMechBossAny = v;
		NPC.downedPlantBoss = v;
		NPC.downedGolemBoss = v;
		NPC.downedFishron = v;
		NPC.downedEmpressOfLight = v;
		NPC.downedAncientCultist = v;
		NPC.downedMoonlord = v;
		NPC.downedGoblins = v;
		NPC.downedFrost = v;
		NPC.downedPirates = v;
		NPC.downedMartians = v;
		NPC.downedClown = v;
	}

	private static void SpawnNpcFromField()
	{
		var q = fields[3].Trim();
		if (q.Length == 0) {
			Say("Escribe un ID o nombre de NPC.");
			return;
		}

		int id;
		if (!(int.TryParse(q, out id) && id > 0 && id < NPCID.Count) && !NPCID.Search.TryGetId(q, out id)) {
			Say($"No encontre el NPC '{q}'.");
			return;
		}

		var p = Main.LocalPlayer;
		NPC.NewNPC(new EntitySource_Misc("AdminPanel"), (int)p.Center.X + 100, (int)p.Center.Y, id);
		Say($"NPC {id} invocado.");
	}

	private static void GiveItem()
	{
		var item = fields[1].Trim();
		if (item.Length == 0)
			return;

		var amount = int.TryParse(fields[2].Trim(), out var n) && n > 0 ? n : 1;

		if (Main.netMode == 1) {
			var arg = item.Contains(' ') ? $"\"{item}\"" : item;
			Run($"/item {arg} {amount}");
			return;
		}

		if (!TryFindItem(item, out var id)) {
			Say($"No encontre el item '{item}'.");
			return;
		}
		SpawnLocal(id, amount);
	}

	private static void QuickGive(string internalName, int amount)
	{
		if (Main.netMode == 1) {
			Run($"/item {internalName} {amount}");
			return;
		}
		if (TryFindItem(internalName, out var id))
			SpawnLocal(id, amount);
		else
			Say($"Item '{internalName}' no encontrado.");
	}

	private static void SpawnLocal(int id, int amount)
	{
		var p = Main.LocalPlayer;
		int max = Math.Max(1, ContentSamples.ItemsByType[id].maxStack);
		var source = new EntitySource_Misc("AdminPanel");
		int left = amount;
		while (left > 0) {
			int stack = Math.Min(left, max);
			p.QuickSpawnItem(source, id, stack);
			left -= stack;
		}
		Say($"{amount} x {Lang.GetItemNameValue(id)}");
	}

	private static bool TryFindItem(string query, out int id)
	{
		if (int.TryParse(query, out id) && id > 0 && id < ItemID.Count)
			return true;

		if (ItemID.Search.TryGetId(query, out id))
			return true;

		int partial = 0;
		for (int i = 1; i < ItemID.Count; i++) {
			var name = Lang.GetItemNameValue(i);
			if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) {
				id = i;
				return true;
			}
			if (partial == 0 && name.Contains(query, StringComparison.OrdinalIgnoreCase))
				partial = i;
		}

		id = partial;
		return partial != 0;
	}

	private static void Run(string command)
	{
		if (Main.netMode != 1) {
			Say("Los comandos solo funcionan en multijugador.");
			return;
		}

		ChatHelper.SendChatMessageFromClient(ChatManager.Commands.CreateOutgoingMessage(command));
		Say(command);
	}

	private static void Say(string text)
	{
		status = text;
		statusTimer = 240;
	}

	// ---------------- Dibujo / entrada ----------------

	private static void HandleToggleKey()
	{
		if (Main.drawingPlayerChat || editing != 0)
			return;

		if (Main.keyState.IsKeyDown(Keys.F8) && Main.oldKeyState.IsKeyUp(Keys.F8))
			Visible = !Visible;
	}

	private static void Section(SpriteBatch sb, Rectangle panel, ref int y, string title)
	{
		Utils.DrawBorderString(sb, title, new Vector2(panel.X + Pad, y), SectionColor, 0.8f);
		y += 18;
	}

	private static void Buttons(SpriteBatch sb, Texture2D pixel, Rectangle panel, ref int y, Point mouse, int cols, params (string Label, Action Do, bool On)[] items)
	{
		int colW = (Width - Pad * (cols + 1)) / cols;
		for (int i = 0; i < items.Length; i++) {
			int col = i % cols, row = i / cols;
			var rect = new Rectangle(panel.X + Pad + col * (colW + Pad), y + row * (RowH + 4), colW, RowH);
			if (DrawButton(sb, pixel, rect, items[i].Label, mouse, items[i].On ? BtnOn : null))
				items[i].Do();
		}
		y += ((items.Length + cols - 1) / cols) * (RowH + 4) + 6;
	}

	private static bool DrawButton(SpriteBatch sb, Texture2D pixel, Rectangle rect, string label, Point mouse, Color? baseColor = null)
	{
		bool hover = rect.Contains(mouse);
		sb.Draw(pixel, rect, hover ? BtnHover : baseColor ?? BtnNormal);

		var font = FontAssets.MouseText.Value;
		float scale = 0.75f;
		var size = font.MeasureString(label) * scale;
		if (size.X > rect.Width - 4) {
			scale *= (rect.Width - 4) / size.X;
			size = font.MeasureString(label) * scale;
		}
		Utils.DrawBorderString(sb, label, new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f + 2f), Color.White, scale);

		if (hover && Clicked) {
			Main.mouseLeftRelease = false;
			StopEditing();
			return true;
		}

		return false;
	}

	private static void Field(SpriteBatch sb, Texture2D pixel, Rectangle rect, int id, string placeholder, Point mouse)
	{
		bool active = editing == id;
		sb.Draw(pixel, rect, active ? new Color(50, 55, 95) : new Color(35, 38, 70));

		string text = fields[id];
		string shown = text;
		if (active && (int)(Main.timeForVisualEffects / 30.0) % 2 == 0)
			shown += "|";

		bool empty = string.IsNullOrEmpty(text) && !active;
		Utils.DrawBorderString(sb, empty ? placeholder : shown, new Vector2(rect.X + 6, rect.Y + 4), empty ? Color.Gray : Color.White, 0.8f);

		if (rect.Contains(mouse) && Clicked) {
			Main.mouseLeftRelease = false;
			editing = id;
			Main.clrInput();
		}
	}

	private static void UpdateTyping()
	{
		PlayerInput.WritingText = true;
		Main.instance.HandleIME();
		fields[editing] = Main.GetInputText(fields[editing]);

		if (Main.inputTextEnter) {
			int id = editing;
			if (id == 1) {
				editing = 2;
				Main.clrInput();
			}
			else {
				StopEditing();
				if (id == 2) GiveItem();
				else if (id == 3) Local(SpawnNpcFromField);
			}
		}
		else if (Main.inputTextEscape) {
			StopEditing();
		}
	}

	private static void StopEditing()
	{
		if (editing == 0)
			return;
		editing = 0;
		Main.clrInput();
	}
}
