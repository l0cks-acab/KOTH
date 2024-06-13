using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("KOTH", "locks", "1.2.0")]
    [Description("KOTH event plugin for Rust with admin commands, a hackable locked crate for the winner, points for kills, and teleport command")]
    public class KOTH : RustPlugin
    {
        private const string EventZoneName = "KOTHZone";
        private const float DefaultEventDuration = 600f; // 10 minutes
        private const float PointInterval = 5f; // Points awarded every 5 seconds
        private const int PointsPerInterval = 10;
        private const int KillPoints = 1; // Points per kill
        private const string HackableLockedCratePrefab = "assets/prefabs/misc/hackable crate/codelockedhackablecrate.prefab";
        private const float TeleportOffset = 10f; // Distance from the center to teleport players
        private const float DefaultEventInterval = 3600f; // 1 hour

        private Vector3 ZoneCenter = new Vector3(0, 0, 0); // Set to desired location
        private float ZoneRadius = 20f; // Set to desired radius

        private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();
        private Timer eventTimer;
        private Timer pointTimer;
        private Timer eventIntervalTimer;
        private BaseEntity hackableLockedCrate;
        private List<ItemDefinition> crateItems;
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
            Config["CrateItems"] = new List<object>
            {
                "rifle.ak",
                "ammo.rifle"
            };
            Config["EventDuration"] = DefaultEventDuration;
            Config["EventInterval"] = DefaultEventInterval;
            SaveConfig();
        }

        private void LoadCrateItems()
        {
            crateItems = new List<ItemDefinition>();
            var itemNames = Config["CrateItems"] as List<object>;
            if (itemNames != null)
            {
                foreach (var itemName in itemNames)
                {
                    var itemDef = ItemManager.FindItemDefinition(itemName.ToString());
                    if (itemDef != null)
                    {
                        crateItems.Add(itemDef);
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
            // Create a visible barrier around the event zone
            foreach (var player in BasePlayer.activePlayerList)
            {
                player.SendConsoleCommand("ddraw.sphere", 10f, Color.red, ZoneCenter, ZoneRadius);
            }
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
            SpawnHackableLockedCrate();
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
                DestroyHackableLockedCrate();
            }

            playerPoints.Clear();
            ScheduleNextEvent();
        }

        private void StopEvent()
        {
            eventTimer?.Destroy();
            pointTimer?.Destroy();
            DestroyHackableLockedCrate();
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
            return Vector3.Distance(player.transform.position, ZoneCenter) <= ZoneRadius;
        }

        private void SpawnHackableLockedCrate()
        {
            hackableLockedCrate = GameManager.server.CreateEntity(HackableLockedCratePrefab, ZoneCenter);
            if (hackableLockedCrate != null)
            {
                hackableLockedCrate.Spawn();
                var storageContainer = hackableLockedCrate.GetComponent<StorageContainer>();
                if (storageContainer != null)
                {
                    storageContainer.inventory.Clear();
                    foreach (var itemDef in crateItems)
                    {
                        storageContainer.inventory.AddItem(itemDef, 1);
                    }
                    hackableLockedCrate.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }
        }

        private void DestroyHackableLockedCrate()
        {
            if (hackableLockedCrate != null && !hackableLockedCrate.IsDestroyed)
            {
                hackableLockedCrate.Kill();
                hackableLockedCrate = null;
            }
        }

        private void UnlockCrateForWinner(BasePlayer winner)
        {
            if (hackableLockedCrate != null && !hackableLockedCrate.IsDestroyed)
            {
                hackableLockedCrate.SetFlag(BaseEntity.Flags.Locked, false);
                winner.ChatMessage("You can now loot the hackable crate at the center of the KOTH event!");
            }
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == hackableLockedCrate && hackableLockedCrate.IsLocked())
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
            if (entity == hackableLockedCrate)
            {
                info.damageTypes = new DamageTypeList();
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
    }
}
