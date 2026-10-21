// Integración nativa de HERO's Mod para Terraria Unified 1.4.5.8.
// Este archivo debe compilarse DENTRO del proyecto Unified (no es un plugin).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;

namespace Terraria.Chat
{
    internal static class HerosModServerCommands
    {
        private sealed class HerosItemSource : IEntitySource
        {
            public string Context => "HerosModServer";
        }

        private const string ItemDataMarker = "[HMITEMDATA]|";
        private const int WorldEditMaxTiles = 10000;
        private sealed class WorldEditSnapshot
        {
            public int X, Y;
            public ushort Type, Wall;
            public bool HasTile;
        }
        private sealed class WorldEditUndoRecord
        {
            public readonly List<WorldEditSnapshot> Tiles = new();
            public int MinX, MinY, MaxX, MaxY;
        }
        private static readonly Dictionary<int, WorldEditUndoRecord> WorldEditUndoByPlayer = new();

        /// <summary>Consume un mensaje de sincronización de atributos enviado por el servidor al cliente.</summary>
        internal static bool TryProcessClientMessage(ChatMessage message)
        {
            if (Main.netMode != 1 || message == null || string.IsNullOrEmpty(message.Text) ||
                !message.Text.StartsWith(ItemDataMarker, StringComparison.Ordinal))
                return false;

            string[] parts = message.Text.Substring(ItemDataMarker.Length).Split('|');
            if (parts.Length < 32 || !int.TryParse(parts[0], out int slot) || slot < 0 || slot >= Main.LocalPlayer.inventory.Length)
                return true;

            Item item = Main.LocalPlayer.inventory[slot];
            if (item == null || item.IsAir)
                return true;

            string[] args = new string[33];
            args[0] = "itemedit";
            args[1] = slot.ToString(CultureInfo.InvariantCulture);
            Array.Copy(parts, 1, args, 2, Math.Min(31, parts.Length - 1));
            ApplyItemEditFields(item, args, 2);
            return true;
        }

        /// <summary>
        /// Invocar desde ChatCommandProcessor.ProcessIncomingMessage en el servidor, antes del
        /// procesamiento normal de comandos. true significa que el mensaje fue consumido.
        /// </summary>
        internal static bool TryProcessIncomingMessage(ChatMessage message, int clientId)
        {
            if (Main.netMode != 2 || message == null || string.IsNullOrWhiteSpace(message.Text))
                return false;

            string text = message.Text.Trim();
            if (!text.StartsWith("/heros", StringComparison.OrdinalIgnoreCase))
                return false;
            if (text.Length > 6 && !char.IsWhiteSpace(text[6]))
                return false;

            if (clientId < 0 || clientId >= Main.maxPlayers || !Main.player[clientId].active)
            {
                Reply(clientId, "Jugador de origen no válido.");
                return true;
            }

            string[] args = Tokenize(text.Length > 6 ? text.Substring(6).Trim() : "");
            if (args.Length == 0)
            {
                Reply(clientId, "Modo anárquico: /heros worldedit fill|clear|replace|undo; item, itemslot, itemedit, favorite, spawnmob, butcher, buff, removebuff, tppos, spawn, time, rain, tp, tphere, kill, heal, kick, clearprojectiles.");
                return true;
            }

            // Modo anárquico: deliberadamente no hay lista blanca ni comprobación de permisos.
            // Cada jugador conectado puede ejecutar las herramientas del panel.

            try
            {
                string action = args[0].ToLowerInvariant();
                Player player = Main.player[clientId];
                switch (action)
                {
                    case "worldedit": return DoWorldEdit(player, args, clientId);
                    case "item": return DoItem(player, args, clientId);
                    case "itemslot": return DoItemSlot(player, args, clientId);
                    case "itemedit": return DoItemEdit(player, args, clientId);
                    case "favorite": return DoFavorite(player, args, clientId);
                    case "spawnmob": return DoSpawnNpc(player, args, clientId);
                    case "butcher": return DoButcher(clientId);
                    case "buff": return DoBuff(player, args, clientId, false);
                    case "removebuff": return DoBuff(player, args, clientId, true);
                    case "tppos": return DoTeleport(player, args, clientId);
                    case "spawn":
                        Teleport(player, Main.spawnTileX, Main.spawnTileY, clientId);
                        Reply(clientId, "Teletransportado al spawn.");
                        return true;
                    case "time": return DoTime(args, clientId);
                    case "rain": return DoRain(args, clientId);
                    case "tp": return DoPlayerTeleport(player, args, clientId, false);
                    case "tphere": return DoPlayerTeleport(player, args, clientId, true);
                    case "kill": return DoKill(args, clientId);
                    case "heal": return DoHeal(args, clientId);
                    case "stats": return DoStats(player, args, clientId);
                    case "kick": return DoKick(args, clientId);
                    case "clearprojectiles": return DoClearProjectiles(clientId, false);
                    case "clearhostileprojectiles": return DoClearProjectiles(clientId, true);
                    case "godmode":
                        Reply(clientId, "Godmode no está habilitado en este parche porque requiere sincronización de estado específica de Unified.");
                        return true;
                    case "mute":
                    case "ban":
                        Reply(clientId, "Mute/ban permanente no están habilitados: requieren un filtro de chat y validación durante la conexión en el servidor.");
                        return true;
                    default:
                        Reply(clientId, "Acción nativa desconocida: " + action);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HerosMod] Error de comando: " + ex);
                Reply(clientId, "Error al ejecutar la acción. Revisa la consola del servidor.");
                return true;
            }
        }

        private static bool DoWorldEdit(Player player, string[] a, int clientId)
        {
            if (a.Length < 2)
            {
                Reply(clientId, "Uso: /heros worldedit fill|clear|replace|undo ...");
                return true;
            }

            string operation = a[1].ToLowerInvariant();
            if (operation == "undo")
            {
                if (!WorldEditUndoByPlayer.TryGetValue(clientId, out WorldEditUndoRecord undo) || undo.Tiles.Count == 0)
                {
                    Reply(clientId, "No tienes una edición WorldEdit para deshacer.");
                    return true;
                }
                foreach (WorldEditSnapshot old in undo.Tiles)
                {
                    if (Main.tile[old.X, old.Y].HasTile)
                        WorldGen.KillTile(old.X, old.Y, false, false, true);
                    if (old.HasTile)
                        WorldGen.PlaceTile(old.X, old.Y, old.Type, true, true);
                    ushort wallNow = Main.tile[old.X, old.Y].WallType;
                    if (wallNow != old.Wall)
                    {
                        if (wallNow != 0) WorldGen.KillWall(old.X, old.Y);
                        if (old.Wall != 0) WorldGen.PlaceWall(old.X, old.Y, old.Wall, true);
                    }
                }
                FrameAndSync(undo.MinX, undo.MinY, undo.MaxX, undo.MaxY);
                WorldEditUndoByPlayer.Remove(clientId);
                Reply(clientId, "Última edición WorldEdit deshecha.");
                return true;
            }

            int expected = operation == "fill" ? 7 : operation == "clear" ? 6 : operation == "replace" ? 8 : -1;
            if (expected < 0 || a.Length < expected || !IntArg(a, 2, out int x1) || !IntArg(a, 3, out int y1) ||
                !IntArg(a, 4, out int x2) || !IntArg(a, 5, out int y2))
            {
                Reply(clientId, "Uso: /heros worldedit fill x1 y1 x2 y2 tileId | clear x1 y1 x2 y2 | replace x1 y1 x2 y2 fromId toId");
                return true;
            }

            int minX = Math.Min(x1, x2), minY = Math.Min(y1, y2);
            int maxX = Math.Max(x1, x2), maxY = Math.Max(y1, y2);
            if (minX < 1 || minY < 1 || maxX >= Main.maxTilesX - 1 || maxY >= Main.maxTilesY - 1)
            {
                Reply(clientId, "Selección fuera de los límites del mundo.");
                return true;
            }
            long area = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (area > WorldEditMaxTiles)
            {
                Reply(clientId, $"Selección de {area} tiles; el máximo por operación es {WorldEditMaxTiles}.");
                return true;
            }

            int fillId = -1, fromId = -1, toId = -1;
            if (operation == "fill" && (!IntArg(a, 6, out fillId) || fillId < 0 || fillId >= TileID.Count))
            { Reply(clientId, "ID de bloque inválido."); return true; }
            if (operation == "replace" && (!IntArg(a, 6, out fromId) || !IntArg(a, 7, out toId) || fromId < 0 || toId < 0 || fromId >= TileID.Count || toId >= TileID.Count))
            { Reply(clientId, "ID origen/destino inválido."); return true; }

            var record = new WorldEditUndoRecord { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];
                record.Tiles.Add(new WorldEditSnapshot { X = x, Y = y, Type = tile.TileType, HasTile = tile.HasTile, Wall = tile.WallType });
            }

            int changed = 0;
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];
                if (operation == "fill")
                {
                    if (tile.HasTile) WorldGen.KillTile(x, y, false, false, true);
                    if (WorldGen.PlaceTile(x, y, fillId, true, true)) changed++;
                }
                else if (operation == "clear")
                {
                    if (tile.HasTile) { WorldGen.KillTile(x, y, false, false, true); changed++; }
                }
                else if (operation == "replace" && tile.HasTile && tile.TileType == fromId)
                {
                    WorldGen.KillTile(x, y, false, false, true);
                    if (WorldGen.PlaceTile(x, y, toId, true, true)) changed++;
                }
            }

            WorldEditUndoByPlayer[clientId] = record;
            FrameAndSync(minX, minY, maxX, maxY);
            Reply(clientId, operation == "clear" ? $"Tiles eliminados: {changed}. Paredes conservadas." : operation == "fill" ? $"Selección rellenada; tiles colocados: {changed}." : $"Tiles reemplazados: {changed}.");
            return true;
        }

        private static void FrameAndSync(int minX, int minY, int maxX, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    WorldGen.SquareTileFrame(x, y);

            // Sincroniza por sectores pequeños para evitar un paquete gigante.
            for (int x = minX; x <= maxX; x += 10)
                for (int y = minY; y <= maxY; y += 10)
                    NetMessage.SendTileSquare(-1, x, y, 10);
        }

        private static string[] Tokenize(string text)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in text)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                }
                else current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result.ToArray();
        }

        private static bool IntArg(string[] a, int i, out int value)
        {
            value = 0;
            return i < a.Length && int.TryParse(a[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        private static bool FloatArg(string[] a, int i, out float value)
        {
            value = 0;
            if (i >= a.Length) return false;
            return float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(a[i], NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
        private static int ClampInt(string[] a, int i, int fallback, int min, int max)
            => IntArg(a, i, out int v) ? Math.Clamp(v, min, max) : fallback;
        private static float ClampFloat(string[] a, int i, float fallback, float min, float max)
            => FloatArg(a, i, out float v) ? Math.Clamp(v, min, max) : fallback;

        private static bool DoItem(Player p, string[] a, int clientId)
        {
            if (a.Length < 3 || !IntArg(a, 1, out int id) || !IntArg(a, 2, out int count) || id <= 0 || id >= ItemID.Count)
            { Reply(clientId, "Uso: /heros item <id> <cantidad> [prefijo]."); return true; }
            count = Math.Clamp(count, 1, 9999);
            int prefix = ClampInt(a, 3, 0, 0, PrefixID.Count - 1);
            int remaining = count;
            int created = 0;
            while (remaining > 0)
            {
                int stack = Math.Min(remaining, 9999);
                int index = Item.NewItem(new HerosItemSource(), (int)p.position.X, (int)p.position.Y, p.width, p.height, id, stack, false, prefix);
                if (index >= 0 && index < Main.item.Length)
                {
                    if (Main.item[index].stack > 0)
                        NetMessage.SendData(MessageID.SyncItem, -1, -1, null, index, 0);
                    created++;
                }
                remaining -= stack;
            }
            Reply(clientId, $"Objeto solicitado: ID {id}, cantidad {count}. Recógelo cerca de tu personaje.");
            return true;
        }

        private static bool DoItemSlot(Player p, string[] a, int clientId)
        {
            if (a.Length < 5 || !IntArg(a, 1, out int slot) || !IntArg(a, 2, out int id) || !IntArg(a, 3, out int stack) || !IntArg(a, 4, out int prefix)
                || slot < 0 || slot >= p.inventory.Length || id <= 0 || id >= ItemID.Count)
            { Reply(clientId, "Uso: /heros itemslot <slot> <id> <cantidad> <prefijo>."); return true; }
            Item item = p.inventory[slot];
            item.SetDefaults(id);
            item.stack = Math.Clamp(stack, 1, Math.Max(1, item.maxStack));
            if (prefix > 0) item.Prefix(Math.Clamp(prefix, 0, PrefixID.Count - 1));
            SyncEquipment(p, slot);
            Reply(clientId, $"Creado {Lang.GetItemNameValue(id)} en el slot {slot}.");
            return true;
        }

        // Args: itemedit slot stack damage defense crit rarity knockback prefix useTime useAnimation useStyle mana shoot shootSpeed scale value pick axe healLife healMana ammo useAmmo createTile createWall buffType buffTime mountType useTurn noMelee noUseGraphic autoReuse consumable
        private static bool DoItemEdit(Player p, string[] a, int clientId)
        {
            if (a.Length < 33 || !IntArg(a, 1, out int slot) || slot < 0 || slot >= p.inventory.Length)
            { Reply(clientId, "Datos de editor incompletos o slot inválido."); return true; }
            Item item = p.inventory[slot];
            if (item == null || item.IsAir) { Reply(clientId, "El slot está vacío; crea primero un objeto en ese slot."); return true; }
            ApplyItemEditFields(item, a, 2);
            SyncEquipment(p, slot);
            SendItemDataToClient(p, slot, clientId);
            Reply(clientId, $"Atributos aplicados y sincronizados en el slot {slot}.");
            return true;
        }

        private static void ApplyItemEditFields(Item item, string[] a, int offset)
        {
            item.stack = Math.Clamp(ClampInt(a, offset, item.stack, 1, Math.Max(1, item.maxStack)), 1, Math.Max(1, item.maxStack));
            int prefix = ClampInt(a, offset + 6, item.prefix, 0, PrefixID.Count - 1);
            if (prefix > 0 && item.prefix != prefix) item.Prefix(prefix);
            item.damage = ClampInt(a, offset + 1, item.damage, 0, 1000000);
            item.defense = ClampInt(a, offset + 2, item.defense, 0, 1000000);
            item.crit = ClampInt(a, offset + 3, item.crit, -100, 1000);
            item.rare = ClampInt(a, offset + 4, item.rare, -11, 20);
            item.knockBack = ClampFloat(a, offset + 5, item.knockBack, 0f, 1000f);
            item.useTime = ClampInt(a, offset + 7, item.useTime, 0, 100000);
            item.useAnimation = ClampInt(a, offset + 8, item.useAnimation, 0, 100000);
            item.useStyle = ClampInt(a, offset + 9, item.useStyle, 0, 1000);
            item.mana = ClampInt(a, offset + 10, item.mana, 0, 100000);
            item.shoot = ClampInt(a, offset + 11, item.shoot, 0, ProjectileID.Count - 1);
            item.shootSpeed = ClampFloat(a, offset + 12, item.shootSpeed, 0f, 10000f);
            item.scale = ClampFloat(a, offset + 13, item.scale, 0.1f, 20f);
            item.value = ClampInt(a, offset + 14, item.value, 0, int.MaxValue);
            item.pick = ClampInt(a, offset + 15, item.pick, 0, 10000);
            item.axe = ClampInt(a, offset + 16, item.axe, 0, 10000);
            item.healLife = ClampInt(a, offset + 17, item.healLife, 0, 100000);
            item.healMana = ClampInt(a, offset + 18, item.healMana, 0, 100000);
            item.ammo = ClampInt(a, offset + 19, item.ammo, 0, 10000);
            item.useAmmo = ClampInt(a, offset + 20, item.useAmmo, 0, 10000);
            item.createTile = ClampInt(a, offset + 21, item.createTile, -1, 10000);
            item.createWall = ClampInt(a, offset + 22, item.createWall, -1, 10000);
            item.buffType = ClampInt(a, offset + 23, item.buffType, 0, BuffID.Count - 1);
            item.buffTime = ClampInt(a, offset + 24, item.buffTime, 0, 10000000);
            item.mountType = ClampInt(a, offset + 25, item.mountType, -1, 10000);
            item.useTurn = ClampInt(a, offset + 26, item.useTurn ? 1 : 0, 0, 1) == 1;
            item.noMelee = ClampInt(a, offset + 27, item.noMelee ? 1 : 0, 0, 1) == 1;
            item.noUseGraphic = ClampInt(a, offset + 28, item.noUseGraphic ? 1 : 0, 0, 1) == 1;
            item.autoReuse = ClampInt(a, offset + 29, item.autoReuse ? 1 : 0, 0, 1) == 1;
            item.consumable = ClampInt(a, offset + 30, item.consumable ? 1 : 0, 0, 1) == 1;
        }

        private static void SendItemDataToClient(Player p, int slot, int clientId)
        {
            Item item = p.inventory[slot];
            string[] fields = {
                slot.ToString(CultureInfo.InvariantCulture),
                item.stack.ToString(CultureInfo.InvariantCulture), item.damage.ToString(CultureInfo.InvariantCulture),
                item.defense.ToString(CultureInfo.InvariantCulture), item.crit.ToString(CultureInfo.InvariantCulture),
                item.rare.ToString(CultureInfo.InvariantCulture), item.knockBack.ToString("R", CultureInfo.InvariantCulture),
                item.prefix.ToString(CultureInfo.InvariantCulture), item.useTime.ToString(CultureInfo.InvariantCulture),
                item.useAnimation.ToString(CultureInfo.InvariantCulture), item.useStyle.ToString(CultureInfo.InvariantCulture),
                item.mana.ToString(CultureInfo.InvariantCulture), item.shoot.ToString(CultureInfo.InvariantCulture),
                item.shootSpeed.ToString("R", CultureInfo.InvariantCulture), item.scale.ToString("R", CultureInfo.InvariantCulture),
                item.value.ToString(CultureInfo.InvariantCulture), item.pick.ToString(CultureInfo.InvariantCulture),
                item.axe.ToString(CultureInfo.InvariantCulture), item.healLife.ToString(CultureInfo.InvariantCulture),
                item.healMana.ToString(CultureInfo.InvariantCulture), item.ammo.ToString(CultureInfo.InvariantCulture),
                item.useAmmo.ToString(CultureInfo.InvariantCulture), item.createTile.ToString(CultureInfo.InvariantCulture),
                item.createWall.ToString(CultureInfo.InvariantCulture), item.buffType.ToString(CultureInfo.InvariantCulture),
                item.buffTime.ToString(CultureInfo.InvariantCulture), item.mountType.ToString(CultureInfo.InvariantCulture),
                item.useTurn ? "1" : "0", item.noMelee ? "1" : "0", item.noUseGraphic ? "1" : "0",
                item.autoReuse ? "1" : "0", item.consumable ? "1" : "0"
            };
            NetMessage.SendData(MessageID.ChatText, clientId, -1, NetworkText.FromLiteral(ItemDataMarker + string.Join("|", fields)), 255, 255, 255);
        }

        private static bool DoFavorite(Player p, string[] a, int clientId)
        {
            if (a.Length < 2 || !IntArg(a, 1, out int slot) || slot < 0 || slot >= p.inventory.Length)
            { Reply(clientId, "Uso: /heros favorite <slot>."); return true; }
            if (p.inventory[slot] == null || p.inventory[slot].IsAir) { Reply(clientId, "El slot está vacío."); return true; }
            p.inventory[slot].favorited = !p.inventory[slot].favorited;
            SyncEquipment(p, slot);
            Reply(clientId, p.inventory[slot].favorited ? "Objeto marcado como favorito." : "Favorito quitado.");
            return true;
        }

        private static bool DoSpawnNpc(Player p, string[] a, int clientId)
        {
            if (a.Length < 3 || !IntArg(a, 1, out int id) || !IntArg(a, 2, out int count) || id <= 0 || id >= NPCID.Count)
            { Reply(clientId, "Uso: /heros spawnmob <npcId> <cantidad>."); return true; }
            count = Math.Clamp(count, 1, 20);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                int index = NPC.NewNPC(new HerosItemSource(), (int)p.Center.X, (int)p.Center.Y, id);
                if (index >= 0 && index < Main.npc.Length)
                {
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, index);
                    spawned++;
                }
            }
            Reply(clientId, $"NPC invocados: {spawned}.");
            return true;
        }

        private static bool DoButcher(int clientId)
        {
            int removed = 0;
            for (int i = 0; i < Main.npc.Length; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && !npc.friendly && !npc.townNPC && npc.lifeMax > 5)
                {
                    npc.active = false;
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, i);
                    removed++;
                }
            }
            Reply(clientId, $"NPC hostiles eliminados: {removed}.");
            return true;
        }

        private static bool DoBuff(Player p, string[] a, int clientId, bool remove)
        {
            if (a.Length < (remove ? 2 : 3) || !IntArg(a, 1, out int id) || id <= 0 || id >= BuffID.Count)
            { Reply(clientId, remove ? "Uso: /heros removebuff <id>." : "Uso: /heros buff <id> <segundos>."); return true; }
            if (remove)
            {
                for (int i = p.buffType.Length - 1; i >= 0; i--) if (p.buffType[i] == id) p.DelBuff(i);
            }
            else
            {
                if (!IntArg(a, 2, out int seconds) || seconds <= 0) { Reply(clientId, "La duración debe ser mayor que cero."); return true; }
                p.AddBuff(id, Math.Clamp(seconds, 1, 86400) * 60);
            }
            NetMessage.SendData(MessageID.PlayerBuffs, -1, -1, null, clientId);
            Reply(clientId, remove ? "Buff eliminado si estaba activo." : "Buff aplicado.");
            return true;
        }

        private static bool DoTeleport(Player p, string[] a, int clientId)
        {
            if (a.Length < 3 || !IntArg(a, 1, out int x) || !IntArg(a, 2, out int y))
            { Reply(clientId, "Uso: /heros tppos <xTiles> <yTiles>."); return true; }
            Teleport(p, x, y, clientId);
            Reply(clientId, $"Teletransportado a ({x}, {y}).");
            return true;
        }

        private static void Teleport(Player p, int tileX, int tileY, int clientId)
        {
            tileX = Math.Clamp(tileX, 10, Main.maxTilesX - 10);
            tileY = Math.Clamp(tileY, 10, Main.maxTilesY - 10);
            int safeY = tileY;
            for (int y = tileY; y < Math.Min(Main.maxTilesY - 4, tileY + 100); y++)
            {
                Tile floor = Main.tile[tileX, y];
                if (floor != null && floor.HasTile && Main.tileSolid[floor.TileType] && !Main.tileSolidTop[floor.TileType]
                    && !Main.tile[tileX, y - 1].HasTile && !Main.tile[tileX, y - 2].HasTile && !Main.tile[tileX, y - 3].HasTile)
                { safeY = y - 3; break; }
            }
            p.Teleport(new Vector2(tileX * 16, safeY * 16), 1);
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, p.whoAmI, p.position.X, p.position.Y, 1);
        }

        private static bool DoTime(string[] a, int clientId)
        {
            if (a.Length < 2) { Reply(clientId, "Uso: /heros time day|night|noon|midnight."); return true; }
            switch (a[1].ToLowerInvariant())
            {
                case "day": Main.dayTime = true; Main.time = 0; break;
                case "noon": Main.dayTime = true; Main.time = 27000; break;
                case "night": Main.dayTime = false; Main.time = 0; break;
                case "midnight": Main.dayTime = false; Main.time = 16200; break;
                default: Reply(clientId, "Tiempo no reconocido."); return true;
            }
            NetMessage.SendData(MessageID.WorldData);
            Reply(clientId, "Hora del mundo actualizada.");
            return true;
        }

        private static bool DoRain(string[] a, int clientId)
        {
            if (a.Length < 2) { Reply(clientId, "Uso: /heros rain start|stop."); return true; }
            if (a[1].Equals("start", StringComparison.OrdinalIgnoreCase)) { Main.raining = true; Main.maxRaining = 0.7f; }
            else if (a[1].Equals("stop", StringComparison.OrdinalIgnoreCase)) { Main.raining = false; Main.maxRaining = 0f; }
            else { Reply(clientId, "Usa start o stop."); return true; }
            NetMessage.SendData(MessageID.WorldData);
            Reply(clientId, "Clima actualizado.");
            return true;
        }

        private static Player FindPlayer(string name)
        {
            return Main.player.FirstOrDefault(p => p != null && p.active && string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool DoPlayerTeleport(Player sender, string[] a, int clientId, bool bring)
        {
            if (a.Length < 2) { Reply(clientId, bring ? "Uso: /heros tphere <jugador>." : "Uso: /heros tp <jugador>."); return true; }
            Player target = FindPlayer(a[1]);
            if (target == null) { Reply(clientId, "No se encontró ese jugador conectado."); return true; }
            Player moved = bring ? target : sender;
            Player destination = bring ? sender : target;
            moved.Teleport(destination.Center, 1);
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, moved.whoAmI, moved.position.X, moved.position.Y, 1);
            Reply(clientId, bring ? $"Traído: {target.name}." : $"Teletransportado hacia {target.name}.");
            return true;
        }

        private static bool DoKill(string[] a, int clientId)
        {
            if (a.Length < 2) { Reply(clientId, "Uso: /heros kill <jugador>."); return true; }
            Player target = FindPlayer(a[1]);
            if (target == null) { Reply(clientId, "No se encontró ese jugador conectado."); return true; }
            target.KillMe(PlayerDeathReason.ByCustomReason($"{target.name} fue eliminado por un administrador."), 1000.0, 0);
            Reply(clientId, $"Eliminado: {target.name}.");
            return true;
        }


        private static bool DoHeal(string[] a, int clientId)
        {
            if (a.Length < 2) { Reply(clientId, "Uso: /heros heal <jugador>."); return true; }
            Player target = FindPlayer(a[1]);
            if (target == null) { Reply(clientId, "No se encontró ese jugador conectado."); return true; }
            target.statLife = target.statLifeMax2;
            target.statMana = target.statManaMax2;
            NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, target.whoAmI);
            Reply(clientId, $"Sanado: {target.name}.");
            return true;
        }

        private static bool DoStats(Player p, string[] a, int clientId)
        {
            if (a.Length < 5 || !IntArg(a, 1, out int life) || !IntArg(a, 2, out int maxLife) || !IntArg(a, 3, out int mana) || !IntArg(a, 4, out int maxMana)
                || maxLife < 1 || maxLife > 1000000 || life < 0 || life > 1000000 || maxMana < 0 || maxMana > 1000000 || mana < 0 || mana > 1000000)
            { Reply(clientId, "Uso: /heros stats <vida> <vidaMax> <mana> <manaMax>."); return true; }
            p.statLifeMax = maxLife;
            p.statLife = Math.Clamp(life, 0, maxLife);
            p.statManaMax = maxMana;
            p.statMana = Math.Clamp(mana, 0, maxMana);
            NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, p.whoAmI);
            Reply(clientId, "Vida y maná actualizados.");
            return true;
        }

        private static bool DoKick(string[] a, int clientId)
        {
            if (a.Length < 2) { Reply(clientId, "Uso: /heros kick <jugador>."); return true; }
            Player target = FindPlayer(a[1]);
            if (target == null) { Reply(clientId, "No se encontró ese jugador conectado."); return true; }
            if (target.whoAmI == clientId) { Reply(clientId, "No puedes expulsarte a ti mismo desde el panel."); return true; }
            NetMessage.SendData(2, target.whoAmI, -1, NetworkText.FromLiteral("Expulsado por un administrador."));
            Reply(clientId, $"Expulsión solicitada: {target.name}.");
            return true;
        }

        private static bool DoClearProjectiles(int clientId, bool hostileOnly)
        {
            int removed = 0;
            foreach (Projectile projectile in Main.projectile)
            {
                if (projectile == null || !projectile.active || (hostileOnly && projectile.friendly)) continue;
                NetMessage.SendData(MessageID.KillProjectile, -1, -1, null, projectile.whoAmI, projectile.owner);
                projectile.active = false;
                removed++;
            }
            Reply(clientId, $"Proyectiles eliminados: {removed}.");
            return true;
        }

        private static void SyncEquipment(Player p, int slot)
        {
            Item item = p.inventory[slot];
            NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, p.whoAmI, slot, item.stack, item.prefix, item.type);
        }

        private static void Reply(int clientId, string text)
        {
            if (clientId >= 0 && clientId < Main.maxPlayers)
                NetMessage.SendData(MessageID.ChatText, clientId, -1, NetworkText.FromLiteral("[HERO's Mod] " + text), 255, 215, 0);
            Console.WriteLine("[HERO's Mod] " + text);
        }
    }
}
