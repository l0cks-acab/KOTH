using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("KOTH", "locks", "1.5.0")]
    [Description("KOTH event plugin for Rust with admin commands, a large wooden box with Boombox skin for the winner, points for kills, and teleport command")]
    public class KOTH : RustPlugin
    {
        [PluginReference]
        private Plugin ZoneManager;

        private const string EventZoneName = "KOTHZone";
        private const float DefaultEventDuration = 600f; // 10 minutes
        private const float PointInterval = 5f; // Points awarded every 5 seconds
        private const int PointsPerInterval = 10;
        private const int KillPoints = 1; // Points per kill
        private const string LargeWoodenBoxPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const ulong BoomboxSkinID = 252490; // Skin ID for Boombox
        private const float TeleportOffset = 10f; // Distance from the center to teleport players
        private const float DefaultEventInterval = 3600f; // 1 hour

        private string kothZoneID;
        private Vector3 ZoneCenter = new Vector3(0, 0, 0); // Set to desired location
        private float ZoneRadius = 20f; // Set to desired radius

        private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();
        private Timer eventTimer;
        private Timer pointTimer;
        private Timer eventIntervalTimer;
        private BaseEntity largeWoodenBox;
        private Dictionary<string, int> crateItems;
        private float eventDuration;
        private float eventInterval;

        void Init()
        {
            LoadDefaultConfig();
            LoadCrateItems();
            ScheduleNextEvent();
        }

        void Unload()
        {
            StopEvent();
            eventIntervalTimer?.Destroy();
        }

        protected override void LoadDefaultConfig()
        {
            Config["CrateItems"] = new Dictionary<string, object>
            {
                {"ammo.rocket.basic", 30}
            };
            Config["EventDuration"] = DefaultEventDuration;
            Config["EventInterval"] = DefaultEventInterval;
            SaveConfig();
        }

        private void LoadCrateItems()
        {
            crateItems = new Dictionary<string, int>();
            var items = Config["CrateItems"] as Dictionary<string, object>;
            if (items != null)
            {
                foreach (var item in items)
                {
                    var itemDef = ItemManager.FindItemDefinition(item.Key);
                    if (itemDef != null)
                    {
                        crateItems.Add(item.Key, Convert.ToInt32(item.Value));
                    }
                }
            }

            float.TryParse(Config["EventDuration"]?.ToString(), out eventDuration);
            float.TryParse(Config["EventInterval"]?.ToString(), out eventInterval);

            if (eventDuration <= 0)
            {
                eventDuration = DefaultEventDuration;
            }

            if (eventInterval <= 0)
            {
                eventInterval = DefaultEventInterval;
            }
        }

        private void CreateEventZone()
        {
            if (kothZoneID != null)
            {
                ZoneManager?.Call("EraseZone", kothZoneID);
            }

            var zoneDefinition = new Dictionary<string, object>
            {
                ["Name"] = "KOTHZone",
                ["Radius"] = ZoneRadius,
                ["Location"] = ZoneCenter,
                ["EnterMessage"] = "You have entered the KOTH zone!",
                ["LeaveMessage"] = "You have left the KOTH zone!"
            };

            kothZoneID = (string)ZoneManager?.Call("CreateOrUpdateZone", EventZoneName, zoneDefinition);
        }

        private void StartEvent()
        {
            if (eventTimer != null)
            {
                PrintToChat("An event is already running!");
                return;
            }

            eventTimer = timer.Once(eventDuration, EndEvent);
            pointTimer = timer.Every(PointInterval, AwardPoints);
            PrintToChat("KOTH event has started! Capture and hold the area to win!");
            SpawnLargeWoodenBox();
            CreateEventZone();
        }

        private void EndEvent()
        {
            StopEvent();
            if (playerPoints.Count > 0)
            {
                var winner = GetEventWinner();
                PrintToChat($"The KOTH event has ended! The winner is {winner.displayName} with {playerPoints[winner.userID]} points!");
                UnlockCrateForWinner(winner);
            }
            else
            {
                PrintToChat("The KOTH event has ended! No participants.");
                DestroyLargeWoodenBox();
            }

            playerPoints.Clear();
            ScheduleNextEvent();
        }

        private void StopEvent()
        {
            eventTimer?.Destroy();
            pointTimer?.Destroy();
            DestroyLargeWoodenBox();
            ZoneManager?.Call("EraseZone", kothZoneID);
            eventTimer = null;
            pointTimer = null;
        }

        private void AwardPoints()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (IsPlayerInZone(player))
                {
                    if (!playerPoints.ContainsKey(player.userID))
                    {
                        playerPoints[player.userID] = 0;
                    }

                    playerPoints[player.userID] += PointsPerInterval;
                    player.ChatMessage($"You have been awarded {PointsPerInterval} points. Total points: {playerPoints[player.userID]}");
                }
            }
        }

        private BasePlayer GetEventWinner()
        {
            ulong winnerId = 0;
            int highestPoints = 0;

            foreach (var entry in playerPoints)
            {
                if (entry.Value > highestPoints)
                {
                    highestPoints = entry.Value;
                    winnerId = entry.Key;
                }
            }

            return BasePlayer.FindByID(winnerId);
        }

        private bool IsPlayerInZone(BasePlayer player)
        {
            return ZoneManager?.Call("isPlayerInZone", EventZoneName, player) is bool inZone && inZone;
        }

        private void SpawnLargeWoodenBox()
        {
            largeWoodenBox = GameManager.server.CreateEntity(LargeWoodenBoxPrefab, ZoneCenter);
            if (largeWoodenBox != null)
            {
                largeWoodenBox.skinID = BoomboxSkinID;
                largeWoodenBox.Spawn();
                var storageContainer = largeWoodenBox.GetComponent<StorageContainer>();
                if (storageContainer != null)
                {
                    storageContainer.inventory.Clear();
                    foreach (var item in crateItems)
                    {
                        var itemDef = ItemManager.FindItemDefinition(item.Key);
                        if (itemDef != null)
                        {
                            storageContainer.inventory.AddItem(itemDef, item.Value);
                        }
                    }
                    largeWoodenBox.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }
        }

        private void DestroyLargeWoodenBox()
        {
            if (largeWoodenBox != null && !largeWoodenBox.IsDestroyed)
            {
                largeWoodenBox.Kill();
                largeWoodenBox = null;
            }
        }

        private void UnlockCrateForWinner(BasePlayer winner)
        {
            if (largeWoodenBox != null && !largeWoodenBox.IsDestroyed)
            {
                largeWoodenBox.SetFlag(BaseEntity.Flags.Locked, false);
                winner.ChatMessage("You can now loot the large wooden box at the center of the KOTH event!");
            }
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == largeWoodenBox && largeWoodenBox.IsLocked())
            {
                var winner = GetEventWinner();
                if (winner != null && player.userID == winner.userID)
                {
                    // Winner can loot the crate
                    return;
                }
                player.ChatMessage("You cannot loot this inventory!");
                NextTick(player.EndLooting);
            }
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == largeWoodenBox)
            {
                info.damageTypes.Clear();
                info.HitEntity = null;
            }
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            var attacker = info?.Initiator as BasePlayer;

            if (victim == null || attacker == null || victim == attacker) return;

            if (IsPlayerInZone(victim) && IsPlayerInZone(attacker))
            {
                if (!playerPoints.ContainsKey(attacker.userID))
                {
                    playerPoints[attacker.userID] = 0;
                }
                playerPoints[attacker.userID] += KillPoints;
                attacker.ChatMessage($"You have been awarded {KillPoints} point(s) for killing {victim.displayName}. Total points: {playerPoints[attacker.userID]}");
            }
        }

        private Vector3 GetRandomPositionNearZone()
        {
            float randomAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2);
            float offsetX = Mathf.Cos(randomAngle) * (ZoneRadius - TeleportOffset);
            float offsetZ = Mathf.Sin(randomAngle) * (ZoneRadius - TeleportOffset);
            return new Vector3(ZoneCenter.x + offsetX, ZoneCenter.y, ZoneCenter.z + offsetZ);
        }

        [ChatCommand("joinkoth")]
        private void JoinKothCommand(BasePlayer player, string command, string[] args)
        {
            if (eventTimer == null)
            {
                player.ChatMessage("No KOTH event is currently running.");
                return;
            }

            Vector3 teleportPosition = GetRandomPositionNearZone();
            player.Teleport(teleportPosition);
            player.ChatMessage("You have been teleported near the KOTH event!");
        }

        [ChatCommand("koth")]
        private void KothCommand(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage("/joinkoth - Teleports you near the KOTH event.");
        }

        [ChatCommand("kothcreate")]
        private void CreateEventCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            if (args.Length != 4 || !float.TryParse(args[0], out float x) || !float.TryParse(args[1], out float y) || !float.TryParse(args[2], out float z) || !float.TryParse(args[3], out float radius))
            {
                player.ChatMessage("Usage: /kothcreate <x> <y> <z> <radius>");
                return;
            }

            ZoneCenter = new Vector3(x, y, z);
            ZoneRadius = radius;

            CreateEventZone();
            StartEvent();
        }

        [ChatCommand("kothcreatehere")]
        private void CreateEventHereCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            if (args.Length != 1 || !float.TryParse(args[0], out float radius))
            {
                player.ChatMessage("Usage: /kothcreatehere <radius>");
                return;
            }

            ZoneCenter = player.transform.position;
            ZoneRadius = radius;

            CreateEventZone();
            StartEvent();
        }

        [ChatCommand("kothstop")]
        private void StopEventCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            StopEvent();
            PrintToChat("KOTH event has been stopped by an admin.");
        }

        private void ScheduleNextEvent()
        {
            eventIntervalTimer = timer.Once(eventInterval, StartEvent);
            PrintToChat($"Next KOTH event will start in {eventInterval / 60} minutes.");
        }

        void OnEnterZone(string ZoneID, BasePlayer player)
        {
            if (ZoneID == kothZoneID)
            {
                player.ChatMessage("You have entered the KOTH zone!");
            }
        }

        void OnExitZone(string ZoneID, BasePlayer player)
        {
            if (ZoneID == kothZoneID)
            {
                player.ChatMessage("You have left the KOTH zone!");
            }
        }

        void OnEntityEnterZone(string ZoneID, BaseEntity entity)
        {
            if (ZoneID == kothZoneID && entity is BasePlayer player)
            {
                player.ChatMessage("You have entered the KOTH zone!");
            }
        }

        void OnEntityExitZone(string ZoneID, BaseEntity entity)
        {
            if (ZoneID == kothZoneID && entity is BasePlayer player)
            {
                player.ChatMessage("You have left the KOTH zone!");
            }
        }
    }
}
