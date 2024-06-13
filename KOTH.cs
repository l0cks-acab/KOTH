using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("KOTH", "locks", "1.0.5")]
    [Description("KOTH event plugin for Rust with GUI scoreboard, admin commands, a locked crate for the winner, and points for kills")]
    public class KOTH : RustPlugin
    {
        private const string EventZoneName = "KOTHZone";
        private const float EventDuration = 600f; // 10 minutes
        private const float PointInterval = 5f; // Points awarded every 5 seconds
        private const int PointsPerInterval = 10;
        private const int KillPoints = 1; // Points per kill
        private const string ScoreboardPanel = "ScoreboardPanel";
        private const string LockedCratePrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";

        private Vector3 ZoneCenter = new Vector3(0, 0, 0); // Set to desired location
        private float ZoneRadius = 20f; // Set to desired radius

        private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();
        private Timer eventTimer;
        private Timer pointTimer;
        private Timer scoreboardTimer;
        private BaseEntity lockedCrate;
        private List<ItemDefinition> crateItems;

        private DynamicConfigFile config;

        void Init()
        {
            config = Config.ReadObject<DynamicConfigFile>() ?? new DynamicConfigFile();
            LoadDefaultConfig();
        }

        void Unload()
        {
            StopEvent();
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config["CrateItems"] = new List<object>
            {
                "rifle.ak",
                "ammo.rifle"
            };
            Config.Save();
            LoadCrateItems();
        }

        private void LoadCrateItems()
        {
            crateItems = new List<ItemDefinition>();
            var itemNames = Config["CrateItems"] as List<object>;
            foreach (var itemName in itemNames)
            {
                var itemDef = ItemManager.FindItemDefinition(itemName.ToString());
                if (itemDef != null)
                {
                    crateItems.Add(itemDef);
                }
            }
        }

        private void CreateEventZone()
        {
            // Logic to create the event zone
            // You can use ZoneManager or similar plugin if available
        }

        private void StartEvent()
        {
            if (eventTimer != null)
            {
                PrintToChat("An event is already running!");
                return;
            }

            eventTimer = timer.Once(EventDuration, EndEvent);
            pointTimer = timer.Every(PointInterval, AwardPoints);
            scoreboardTimer = timer.Every(1f, UpdateScoreboard);
            PrintToChat("KOTH event has started! Capture and hold the area to win!");
            SpawnLockedCrate();
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
                DestroyLockedCrate();
            }

            playerPoints.Clear();
        }

        private void StopEvent()
        {
            eventTimer?.Destroy();
            pointTimer?.Destroy();
            scoreboardTimer?.Destroy();
            DestroyAllScoreboards();
            DestroyLockedCrate();
            eventTimer = null;
            pointTimer = null;
            scoreboardTimer = null;
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

        private void UpdateScoreboard()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyScoreboard(player);
                CreateScoreboard(player);
            }
        }

        private void CreateScoreboard(BasePlayer player)
        {
            var elements = new CuiElementContainer();
            var panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.7" },
                RectTransform = { AnchorMin = "0.8 0.8", AnchorMax = "0.99 0.99" },
                CursorEnabled = false
            }, "Overlay", ScoreboardPanel);

            var sortedPoints = playerPoints.OrderByDescending(kv => kv.Value).Take(5).ToList();
            var yPos = 0.8f;
            foreach (var kv in sortedPoints)
            {
                var playerName = BasePlayer.FindByID(kv.Key)?.displayName ?? "Unknown";
                var text = $"{playerName}: {kv.Value} points";
                elements.Add(new CuiLabel
                {
                    Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleLeft },
                    RectTransform = { AnchorMin = $"0.1 {yPos}", AnchorMax = $"0.9 {yPos + 0.05f}" }
                }, panel);
                yPos -= 0.05f;
            }

            CuiHelper.AddUi(player, elements);
        }

        private void DestroyScoreboard(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, ScoreboardPanel);
        }

        private void DestroyAllScoreboards()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyScoreboard(player);
            }
        }

        private void SpawnLockedCrate()
        {
            lockedCrate = GameManager.server.CreateEntity(LockedCratePrefab, ZoneCenter);
            if (lockedCrate != null)
            {
                lockedCrate.Spawn();
                var storageContainer = lockedCrate.GetComponent<StorageContainer>();
                if (storageContainer != null)
                {
                    storageContainer.inventory.Clear();
                    foreach (var itemDef in crateItems)
                    {
                        var item = ItemManager.Create(itemDef, 1);
                        storageContainer.inventory.AddItem(item);
                    }
                    lockedCrate.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }
        }

        private void DestroyLockedCrate()
        {
            if (lockedCrate != null && !lockedCrate.IsDestroyed)
            {
                lockedCrate.Kill();
                lockedCrate = null;
            }
        }

        private void UnlockCrateForWinner(BasePlayer winner)
        {
            if (lockedCrate != null && !lockedCrate.IsDestroyed)
            {
                lockedCrate.SetFlag(BaseEntity.Flags.Locked, false);
                winner.ChatMessage("You can now loot the crate at the center of the KOTH event!");
            }
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == lockedCrate && lockedCrate.IsLocked())
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
    }
}
