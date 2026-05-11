using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForgottenQuests;

public class QuestServerManager
{
    private const string ConfigName = "forgottenquests.json";
    private const string ProgressConfigName = "forgottenquests-progress.json";
    private const string KillProgressConfigName = "forgottenquests-kill-progress.json";
    private const string SubmitProgressConfigName = "forgottenquests-submit-progress.json";
    private const string ReadyNotificationConfigName = "forgottenquests-ready-notifications.json";

    private readonly ICoreServerAPI sapi;
    private List<QuestData> quests = new();
    private Dictionary<string, Dictionary<string, long>> progress = new();
    private Dictionary<string, Dictionary<string, int>> killProgress = new();
    private Dictionary<string, Dictionary<string, int>> submitProgress = new();
    private Dictionary<string, Dictionary<string, long>> readyNotifications = new();

    public QuestServerManager(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        LoadQuests();
        LoadProgress();
        LoadKillProgress();
        LoadSubmitProgress();
        LoadReadyNotifications();

        sapi.Event.RegisterGameTickListener(CheckPassiveObjectiveReadiness, 2000);

        sapi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SetMessageHandler<RequestQuestListPacket>(OnRequestQuestList)
            .SetMessageHandler<SaveQuestPacket>(OnSaveQuest)
            .SetMessageHandler<DeleteQuestPacket>(OnDeleteQuest)
            .SetMessageHandler<ClaimQuestRewardPacket>(OnClaimQuestReward)
            .SetMessageHandler<SubmitQuestItemPacket>(OnSubmitQuestItem);

        sapi.Event.OnEntityDeath += OnEntityDeath;
    }

    private string ConfigFolder => Path.Combine(GamePaths.ModConfig, "ForgottenQuests");

    private string GetConfigPath(string fileName) => Path.Combine(ConfigFolder, fileName);

    private T LoadConfigFromFolder<T>(string fileName, T fallback)
    {
        Directory.CreateDirectory(ConfigFolder);

        string path = GetConfigPath(fileName);
        if (!File.Exists(path)) return fallback;

        string json = File.ReadAllText(path);
        return JsonUtil.FromString<T>(json) ?? fallback;
    }

    private void SaveConfigToFolder<T>(T data, string fileName)
    {
        Directory.CreateDirectory(ConfigFolder);
        File.WriteAllText(GetConfigPath(fileName), JsonUtil.ToString(data));
    }

    private void LoadQuests()
    {
        quests = LoadConfigFromFolder(ConfigName, new List<QuestData>());
        foreach (QuestData quest in quests) EnsureRewardSlotSize(quest);
    }

    private void SaveQuests() => SaveConfigToFolder(quests, ConfigName);

    private static void EnsureRewardSlotSize(QuestData quest)
    {
        QuestItemStackData?[] oldSlots = quest.RewardSlots ?? Array.Empty<QuestItemStackData?>();
        if (oldSlots.Length == 9) return;
        QuestItemStackData?[] newSlots = new QuestItemStackData?[9];
        for (int i = 0; i < oldSlots.Length && i < newSlots.Length; i++) newSlots[i] = oldSlots[i];
        quest.RewardSlots = newSlots;
    }

    private void LoadProgress()
    {
        progress = LoadConfigFromFolder(ProgressConfigName, new Dictionary<string, Dictionary<string, long>>());
    }

    private void SaveProgress() => SaveConfigToFolder(progress, ProgressConfigName);

    private void LoadKillProgress()
    {
        killProgress = LoadConfigFromFolder(KillProgressConfigName, new Dictionary<string, Dictionary<string, int>>());
    }

    private void SaveKillProgress() => SaveConfigToFolder(killProgress, KillProgressConfigName);

    private void LoadSubmitProgress()
    {
        submitProgress = LoadConfigFromFolder(SubmitProgressConfigName, new Dictionary<string, Dictionary<string, int>>());
    }

    private void SaveSubmitProgress() => SaveConfigToFolder(submitProgress, SubmitProgressConfigName);

    private void LoadReadyNotifications()
    {
        readyNotifications = LoadConfigFromFolder(ReadyNotificationConfigName, new Dictionary<string, Dictionary<string, long>>());
    }

    private void SaveReadyNotifications() => SaveConfigToFolder(readyNotifications, ReadyNotificationConfigName);

    private Dictionary<string, long> GetPlayerReadyNotifications(IServerPlayer player)
    {
        if (!readyNotifications.TryGetValue(player.PlayerUID, out Dictionary<string, long>? playerNotifications))
        {
            playerNotifications = new Dictionary<string, long>();
            readyNotifications[player.PlayerUID] = playerNotifications;
        }
        return playerNotifications;
    }

    private void OnRequestQuestList(IServerPlayer fromPlayer, RequestQuestListPacket packet) => SendQuestList(fromPlayer);

    private void SendQuestList(IServerPlayer player)
    {
        sapi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new QuestListPacket
            {
                QuestsJson = JsonUtil.ToString(quests),
                StatesJson = JsonUtil.ToString(BuildStatesFor(player)),
                CanEdit = CanEditQuests(player)
            }, player);
    }

    private List<QuestClientState> BuildStatesFor(IServerPlayer player)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dictionary<string, long> playerProgress = GetPlayerProgress(player);
        Dictionary<string, int> playerKills = GetPlayerKillProgress(player);
        Dictionary<string, int> playerSubmits = GetPlayerSubmitProgress(player);

        return quests.Select(quest =>
        {
            playerProgress.TryGetValue(quest.Id, out long lastCompleted);
            long cooldownSeconds = Math.Max(0, quest.CooldownHours) * 3600L;
            long remaining = Math.Max(0, lastCompleted + cooldownSeconds - now);
            int objectiveProgress = GetObjectiveProgress(player, quest, playerKills, playerSubmits);

            return new QuestClientState
            {
                QuestId = quest.Id,
                CanClaim = remaining <= 0,
                RemainingSeconds = remaining,
                LastCompletedUnixTime = lastCompleted,
                ObjectiveProgress = objectiveProgress,
                ObjectiveReady = remaining <= 0 && IsObjectiveReady(player, quest, objectiveProgress)
            };
        }).ToList();
    }

    private Dictionary<string, long> GetPlayerProgress(IServerPlayer player)
    {
        if (!progress.TryGetValue(player.PlayerUID, out Dictionary<string, long>? playerProgress))
        {
            playerProgress = new Dictionary<string, long>();
            progress[player.PlayerUID] = playerProgress;
        }
        return playerProgress;
    }

    private Dictionary<string, int> GetPlayerKillProgress(IServerPlayer player)
    {
        if (!killProgress.TryGetValue(player.PlayerUID, out Dictionary<string, int>? playerKills))
        {
            playerKills = new Dictionary<string, int>();
            killProgress[player.PlayerUID] = playerKills;
        }
        return playerKills;
    }

    private Dictionary<string, int> GetPlayerSubmitProgress(IServerPlayer player)
    {
        if (!submitProgress.TryGetValue(player.PlayerUID, out Dictionary<string, int>? playerSubmits))
        {
            playerSubmits = new Dictionary<string, int>();
            submitProgress[player.PlayerUID] = playerSubmits;
        }
        return playerSubmits;
    }

    private int GetObjectiveProgress(IServerPlayer player, QuestData quest, Dictionary<string, int> playerKills, Dictionary<string, int> playerSubmits)
    {
        if (quest.TaskType == QuestTaskType.KillEntity)
        {
            playerKills.TryGetValue(quest.Id, out int count);
            return count;
        }
        if (quest.TaskType == QuestTaskType.CollectItem)
        {
            return CountMatchingItems(player, quest.TargetStack);
        }
        if (quest.TaskType == QuestTaskType.SubmitItem)
        {
            playerSubmits.TryGetValue(quest.Id, out int count);
            return count;
        }
        return 0;
    }

    private bool IsObjectiveReady(IServerPlayer player, QuestData quest, int progressValue)
    {
        return quest.TaskType switch
        {
            QuestTaskType.CollectItem => progressValue >= Math.Max(1, quest.TargetAmount),
            QuestTaskType.SubmitItem => progressValue >= Math.Max(1, quest.TargetAmount),
            QuestTaskType.KillEntity => progressValue >= Math.Max(1, quest.TargetAmount),
            QuestTaskType.ReachPosition => IsPlayerAtQuestPosition(player, quest),
            QuestTaskType.CustomText => true,
            _ => false
        };
    }


    private static bool CanEditQuests(IServerPlayer player)
    {
        return player.WorldData.CurrentGameMode == EnumGameMode.Creative
            || player.HasPrivilege(ForgottenQuestsModSystem.QuestGiverPrivilege);
    }

        private void OnDeleteQuest(IServerPlayer player, DeleteQuestPacket packet)
        {
            if (!CanEditQuests(player))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "Нет прав на удаление квестов.", EnumChatType.Notification);
                return;
            }

            if (packet == null || string.IsNullOrWhiteSpace(packet.QuestId)) return;

            int removed = quests.RemoveAll(q => q.Id == packet.QuestId);
            if (removed <= 0) return;

            SaveQuests();
            BroadcastQuestLists();
        }


    private void OnSaveQuest(IServerPlayer fromPlayer, SaveQuestPacket packet)
    {
        if (!CanEditQuests(fromPlayer))
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, "Для настройки заданий нужен Creative или привилегия QuestGiver.", EnumChatType.Notification);
            return;
        }

        QuestData? quest = JsonUtil.FromString<QuestData>(packet.Json);
        if (quest == null) return;
        EnsureRewardSlotSize(quest);

        int index = quests.FindIndex(q => q.Id == quest.Id);
        if (index >= 0) quests[index] = quest;
        else quests.Add(quest);

        SaveQuests();
        BroadcastQuestLists();
    }

    private void BroadcastQuestLists()
    {
        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers.Cast<IServerPlayer>()) SendQuestList(player);
    }

    private void OnSubmitQuestItem(IServerPlayer fromPlayer, SubmitQuestItemPacket packet)
    {
        QuestData? quest = quests.FirstOrDefault(q => q.Id == packet.QuestId);
        if (quest == null) return;

        if (quest.TaskType != QuestTaskType.SubmitItem)
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, "Сдача предмета доступна только для задания типа SubmitItem / Доставить предмет.", EnumChatType.Notification);
            return;
        }

        if (!IsCooldownReady(fromPlayer, quest, out long remaining))
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"Задание на откате. Осталось: {FormatRemaining(remaining)}.", EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        int need = Math.Max(1, quest.TargetAmount);

        Dictionary<string, int> playerSubmits = GetPlayerSubmitProgress(fromPlayer);
        playerSubmits.TryGetValue(quest.Id, out int alreadySubmitted);
        if (alreadySubmitted >= need)
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, "Предмет уже сдан. Нажми кнопку Завершить, чтобы получить награду.", EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        int haveInRealInventory = CountMatchingItems(fromPlayer, quest.TargetStack);
        if (haveInRealInventory < need)
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"Предмет задания не найден в инвентаре в нужном количестве. Есть: {haveInRealInventory}/{need}.", EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        if (!RemoveMatchingItems(fromPlayer, quest.TargetStack, need))
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, "Не удалось забрать предмет задания из инвентаря.", EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        playerSubmits[quest.Id] = need;
        SaveSubmitProgress();
        TryNotifyObjectiveReady(fromPlayer, quest);
        SendQuestList(fromPlayer);
    }

    private void OnClaimQuestReward(IServerPlayer fromPlayer, ClaimQuestRewardPacket packet)
    {
        QuestData? quest = quests.FirstOrDefault(q => q.Id == packet.QuestId);
        if (quest == null) return;

        if (!IsCooldownReady(fromPlayer, quest, out long remaining))
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"Задание на откате. Осталось: {FormatRemaining(remaining)}.", EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        if (quest.TaskType == QuestTaskType.CollectItem)
        {
            int have = CountMatchingItems(fromPlayer, quest.TargetStack);
            int need = Math.Max(1, quest.TargetAmount);
            if (have < need)
            {
                fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, $"Указанный предмет не найден в нужном количестве. Есть: {have}/{need}.", EnumChatType.Notification);
                SendQuestList(fromPlayer);
                return;
            }

            CompleteQuest(fromPlayer, quest);
            return;
        }

        int objectiveProgress = GetObjectiveProgress(fromPlayer, quest, GetPlayerKillProgress(fromPlayer), GetPlayerSubmitProgress(fromPlayer));
        if (!IsObjectiveReady(fromPlayer, quest, objectiveProgress))
        {
            string msg = quest.TaskType == QuestTaskType.ReachPosition
                ? "Ты не находишься в нужной точке координат."
                : quest.TaskType == QuestTaskType.KillEntity
                    ? $"Нужная цель ещё не убита. Прогресс: {objectiveProgress}/{Math.Max(1, quest.TargetAmount)}."
                    : quest.TaskType == QuestTaskType.SubmitItem
                        ? "Сначала сдай предмет кнопкой в окне задания."
                        : "Условие задания ещё не выполнено.";
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
            SendQuestList(fromPlayer);
            return;
        }

        CompleteQuest(fromPlayer, quest);
    }

    private bool IsCooldownReady(IServerPlayer player, QuestData quest, out long remaining)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dictionary<string, long> playerProgress = GetPlayerProgress(player);
        playerProgress.TryGetValue(quest.Id, out long lastCompleted);
        long cooldownSeconds = Math.Max(0, quest.CooldownHours) * 3600L;
        remaining = Math.Max(0, lastCompleted + cooldownSeconds - now);
        return remaining <= 0;
    }

    private void CompleteQuest(IServerPlayer player, QuestData quest)
    {
        GiveRewards(player, quest);
        GetPlayerProgress(player)[quest.Id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (quest.TaskType == QuestTaskType.KillEntity)
        {
            GetPlayerKillProgress(player)[quest.Id] = 0;
            SaveKillProgress();
        }

        if (quest.TaskType == QuestTaskType.SubmitItem)
        {
            GetPlayerSubmitProgress(player)[quest.Id] = 0;
            SaveSubmitProgress();
        }

        long completedAt = GetPlayerProgress(player)[quest.Id];
        GetPlayerReadyNotifications(player)[quest.Id] = completedAt;
        SaveReadyNotifications();
        SaveProgress();
        SendQuestList(player);
    }

    private void GiveRewards(IServerPlayer player, QuestData quest)
    {
        foreach (QuestItemStackData? reward in quest.RewardSlots)
        {
            ItemStack? stack = reward?.ToItemStack(sapi);
            if (stack == null || stack.StackSize <= 0) continue;
            bool fullyMoved = player.InventoryManager.TryGiveItemstack(stack, true);
            if (!fullyMoved && stack.StackSize > 0) sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.Add(0, 0.5, 0));
        }
    }

    private void OnEntityDeath(Entity entity, DamageSource damageSource)
    {
        if (entity?.Code == null) return;

        IServerPlayer? player = FindKillerPlayer(damageSource, entity);
        if (player == null)
        {
            sapi.Logger.Notification($"[ForgottenQuests] Kill quest ignored: killer not found for entity {entity.Code}");
            return;
        }

        string killedCode = entity.Code?.ToString() ?? "";
        Dictionary<string, int> playerKills = GetPlayerKillProgress(player);
        bool changed = false;

        foreach (QuestData quest in quests.Where(q => q.TaskType == QuestTaskType.KillEntity))
        {
            if (!IsCooldownReady(player, quest, out _)) continue;
            if (!KilledEntityMatchesQuest(killedCode, quest)) continue;
            playerKills.TryGetValue(quest.Id, out int current);
            int need = Math.Max(1, quest.TargetAmount);
            int next = Math.Min(need, current + 1);
            playerKills[quest.Id] = next;
            player.SendMessage(GlobalConstants.GeneralChatGroup, $"Задание '{quest.Title}': убийства {next}/{need}.", EnumChatType.Notification);
            if (next >= need) TryNotifyObjectiveReady(player, quest);
            changed = true;
        }

        if (changed)
        {
            SaveKillProgress();
            SendQuestList(player);
        }
    }


    private IServerPlayer? FindKillerPlayer(DamageSource damageSource, Entity killedEntity)
    {
        if (damageSource != null)
        {
            // Метод из API VS 1.22: работает и для ближнего боя, и для снарядов.
            IServerPlayer? player = ExtractServerPlayer(damageSource.GetCauseEntity());
            if (player != null) return player;

            player = ExtractServerPlayer(damageSource.SourceEntity);
            if (player != null) return player;

            player = ExtractServerPlayer(damageSource.CauseEntity);
            if (player != null) return player;

            // Fallback на случай, если другой мод кладёт игрока в нестандартное поле.
            foreach (string name in new[] { "SourcePlayer", "Player", "CausePlayer", "ByPlayer", "Attacker", "Source", "ByEntity" })
            {
                object? value = ReadMember(damageSource, name);
                player = ExtractServerPlayer(value);
                if (player != null) return player;
            }
        }

        // Fallback по принципу quest mods: если игра не передала убийцу,
        // засчитываем ближайшего живого игрока рядом с погибшим существом.
        // Это спасает случаи, когда смерть пришла через нестандартный DamageSource.
        return FindNearestPlayerTo(killedEntity, 12);
    }

    private IServerPlayer? FindNearestPlayerTo(Entity entity, double maxDistance)
    {
        IServerPlayer? nearest = null;
        double bestSq = maxDistance * maxDistance;

        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            if (player.Entity == null || !player.Entity.Alive) continue;
            double distSq = player.Entity.Pos.XYZ.SquareDistanceTo(entity.Pos.XYZ);
            if (distSq > bestSq) continue;
            bestSq = distSq;
            nearest = player;
        }

        return nearest;
    }

    private static object? ReadMember(object obj, string name)
    {
        Type type = obj.GetType();
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj)
            ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
    }

    private static IServerPlayer? ExtractServerPlayer(object? value)
    {
        if (value is IServerPlayer serverPlayer) return serverPlayer;
        if (value is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer ep) return ep;
        if (value is IPlayer player && player is IServerPlayer sp) return sp;

        object? nestedPlayer = value == null ? null : ReadMember(value, "Player");
        if (nestedPlayer is IServerPlayer nestedServerPlayer) return nestedServerPlayer;
        if (nestedPlayer is EntityPlayer nestedEntityPlayer && nestedEntityPlayer.Player is IServerPlayer nestedSp) return nestedSp;

        return null;
    }

    private static bool KilledEntityMatchesQuest(string killedEntityCode, QuestData quest)
    {
        string target = quest.TargetStack?.GetQuestTargetCode() ?? quest.TargetStack?.Code ?? "";

        // Совместимость со старыми конфигами, где цель убийства была записана отдельной строкой.
        if (string.IsNullOrWhiteSpace(target)) target = quest.KillEntityCode ?? "";
        if (string.IsNullOrWhiteSpace(target)) return false;

        string targetPath = NormalizeCodePath(target);
        string killedPath = NormalizeCodePath(killedEntityCode);

        // Простое сравнение, как до добавления лишнего режима сравнения ID.
        // Поддерживает настройку через предмет/метку существа или дроп: pig, wolf, drifter и т.п.
        return killedPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)
            || killedPath.Contains(targetPath, StringComparison.OrdinalIgnoreCase)
            || targetPath.Contains(killedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodePath(string code)
    {
        string path = code.Contains(':') ? code.Split(':').Last() : code;
        path = path.ToLowerInvariant();
        foreach (string prefix in new[] { "item-", "block-", "creature-", "entity-", "mob-", "spawn-", "egg-", "meat-", "raw-" })
        {
            if (path.StartsWith(prefix)) path = path[prefix.Length..];
        }
        return path.Replace("_", "-");
    }

    private bool IsPlayerAtQuestPosition(IServerPlayer player, QuestData quest)
    {
        double radius = Math.Max(1, quest.Radius);
        Vec3d pos = player.Entity.Pos.XYZ;

        // Основная проверка: реальные абсолютные координаты мира.
        if (DistanceSq(pos.X, pos.Y, pos.Z, quest.TargetX, quest.TargetY, quest.TargetZ) <= radius * radius) return true;

        // Если Y оставлен 0, проверяем только X/Z. Это удобно для отметки точки без высоты.
        if (Math.Abs(quest.TargetY) < 0.001)
        {
            double dx = pos.X - quest.TargetX;
            double dz = pos.Z - quest.TargetZ;
            if (dx * dx + dz * dz <= radius * radius) return true;
        }

        // Дополнительная проверка для координат относительно спавна, которые часто видит игрок в интерфейсе.
        Vec3d? spawn = TryGetSpawnPosition();
        if (spawn != null)
        {
            double rx = pos.X - spawn.X;
            double ry = pos.Y - spawn.Y;
            double rz = pos.Z - spawn.Z;
            if (DistanceSq(rx, ry, rz, quest.TargetX, quest.TargetY, quest.TargetZ) <= radius * radius) return true;

            if (Math.Abs(quest.TargetY) < 0.001)
            {
                double dx = rx - quest.TargetX;
                double dz = rz - quest.TargetZ;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
        }

        return false;
    }

    private Vec3d? TryGetSpawnPosition()
    {
        foreach (string propName in new[] { "DefaultSpawnPosition", "SpawnPosition" })
        {
            object? value = sapi.World.GetType().GetProperty(propName)?.GetValue(sapi.World);
            if (value is EntityPos ep) return ep.XYZ;
            if (value is Vec3d vec) return vec;
            if (value is BlockPos bp) return new Vec3d(bp.X, bp.Y, bp.Z);
        }
        return null;
    }

    private static double DistanceSq(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return dx * dx + dy * dy + dz * dz;
    }

    private int CountMatchingItems(IServerPlayer player, QuestItemStackData? target)
    {
        if (target == null) return 0;
        int count = 0;
        foreach (ItemSlot slot in EnumeratePlayerSlots(player))
        {
            if (StackMatches(slot.Itemstack, target)) count += slot.Itemstack.StackSize;
        }
        return count;
    }

    private bool RemoveMatchingItems(IServerPlayer player, QuestItemStackData? target, int amount)
    {
        if (target == null || amount <= 0) return false;
        int remaining = amount;
        foreach (ItemSlot slot in EnumeratePlayerSlots(player))
        {
            if (!StackMatches(slot.Itemstack, target)) continue;
            int take = Math.Min(remaining, slot.Itemstack.StackSize);
            slot.TakeOut(take);
            slot.MarkDirty();
            remaining -= take;
            if (remaining <= 0) return true;
        }
        return false;
    }

    private static bool StackMatches(ItemStack? stack, QuestItemStackData target)
    {
        if (stack == null || stack.Collectible == null) return false;

        string stackCode = stack.Collectible.Code?.ToString() ?? "";
        bool codeMatches = string.Equals(stackCode, target.Code, StringComparison.OrdinalIgnoreCase);
        bool classMatches = string.IsNullOrWhiteSpace(target.Class)
            || string.Equals(stack.Class.ToString(), target.Class, StringComparison.OrdinalIgnoreCase);

        return codeMatches && classMatches;
    }

    private static IEnumerable<ItemSlot> EnumeratePlayerSlots(IServerPlayer player)
    {
        if (player?.InventoryManager?.InventoriesOrdered == null) yield break;

        foreach (InventoryBase inventory in player.InventoryManager.InventoriesOrdered)
        {
            if (inventory == null) continue;

            string invId = inventory.InventoryID ?? "";
            string className = inventory.ClassName ?? "";
            string invCode = inventory.GetType().Name ?? "";

            // Не считаем временные GUI-инвентари ForgottenQuests и креативный каталог.
            // В 1.22.2 InventoryPlayerCreative может падать на Count при серверном переборе,
            // поэтому его обязательно пропускаем до foreach по слотам.
            if (invId.StartsWith("forgottenquests", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("forgottenquests", StringComparison.OrdinalIgnoreCase)
                || invCode.IndexOf("Creative", StringComparison.OrdinalIgnoreCase) >= 0
                || invId.IndexOf("creative", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("creative", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            IEnumerator<ItemSlot>? enumerator = null;
            try
            {
                enumerator = inventory.GetEnumerator();
            }
            catch
            {
                continue;
            }

            while (true)
            {
                ItemSlot? slot = null;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    slot = enumerator.Current;
                }
                catch
                {
                    break;
                }

                if (slot != null) yield return slot;
            }
        }
    }


    private void CheckPassiveObjectiveReadiness(float dt)
    {
        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            foreach (QuestData quest in quests)
            {
                if (quest.TaskType != QuestTaskType.CollectItem
                    && quest.TaskType != QuestTaskType.ReachPosition
                    && quest.TaskType != QuestTaskType.CustomText)
                {
                    continue;
                }

                TryNotifyObjectiveReady(player, quest);
            }
        }
    }

    private void TryNotifyObjectiveReady(IServerPlayer player, QuestData quest)
    {
        if (!IsCooldownReady(player, quest, out _)) return;

        int objectiveProgress = GetObjectiveProgress(player, quest, GetPlayerKillProgress(player), GetPlayerSubmitProgress(player));
        if (!IsObjectiveReady(player, quest, objectiveProgress)) return;

        Dictionary<string, long> playerProgress = GetPlayerProgress(player);
        playerProgress.TryGetValue(quest.Id, out long lastCompleted);

        Dictionary<string, long> playerNotifications = GetPlayerReadyNotifications(player);
        if (playerNotifications.TryGetValue(quest.Id, out long notifiedForCompletionTime)
            && notifiedForCompletionTime == lastCompleted)
        {
            return;
        }

        playerNotifications[quest.Id] = lastCompleted;
        SaveReadyNotifications();

        sapi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new QuestCompletedClientPacket { QuestTitle = quest.Title }, player);
    }

    private static string FormatRemaining(long seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours}ч {time.Minutes}м";
        if (time.TotalMinutes >= 1) return $"{time.Minutes}м {time.Seconds}с";
        return $"{time.Seconds}с";
    }
}
