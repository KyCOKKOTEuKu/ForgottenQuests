using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForgottenQuests;

public class GuiDialogQuestDetails : GuiDialog
{
    private readonly QuestData quest;
    private readonly QuestClientState state;
    private readonly InventoryGeneric rewardsInventory;
    private readonly InventoryGeneric targetInventory;

    public override string ToggleKeyCombinationCode => null;

    public GuiDialogQuestDetails(ICoreClientAPI capi, QuestData quest, QuestClientState state) : base(capi)
    {
        this.quest = quest;
        this.state = state;

        rewardsInventory = new InventoryGeneric(9, "forgottenquests-view-rewards-" + quest.Id, capi);
        rewardsInventory.TakeLocked = true;
        rewardsInventory.PutLocked = true;
        FillInventory(rewardsInventory, quest.RewardSlots);

        targetInventory = new InventoryGeneric(1, "forgottenquests-view-target-" + quest.Id, capi);
        targetInventory.TakeLocked = true;
        targetInventory.PutLocked = true;
        targetInventory[0].Itemstack = quest.TargetStack?.ToItemStack(capi);
        targetInventory[0].MarkDirty();


        ComposeDialog();
    }

    private void FillInventory(InventoryGeneric inventory, QuestItemStackData?[]? stacks)
    {
        if (stacks == null) return;
        for (int i = 0; i < inventory.Count && i < stacks.Length; i++)
        {
            inventory[i].Itemstack = stacks[i]?.ToItemStack(capi);
            inventory[i].MarkDirty();
        }
    }

    private void ComposeDialog()
    {
        ElementBounds bg = ElementBounds.Fixed(0, 0, 820, 650);
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

        bool canComplete = state.CanClaim && state.ObjectiveReady;
        string targetHeader = GetTargetHeader();
        string targetHint = GetTargetHint();
        bool showTargetSlot = quest.TaskType == QuestTaskType.KillEntity
            || quest.TaskType == QuestTaskType.CollectItem
            || quest.TaskType == QuestTaskType.SubmitItem;

        SingleComposer = capi.Gui.CreateCompo("forgottenquests-details-" + quest.Id, dialogBounds)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(quest.Title, OnTitleBarClose)
            .BeginChildElements(bg)

            .AddStaticText(quest.Title, CairoFont.WhiteMediumText(), ElementBounds.Fixed(30, 45, 740, 28), "questtitle")
            .AddStaticText("Описание", CairoFont.WhiteMediumText(), ElementBounds.Fixed(30, 82, 220, 28))
            .AddRichtext(quest.Description, CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 115, 730, 90), "description")

            .AddStaticText("Тип: " + TaskTypeToRu(quest.TaskType), CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 220, 300, 25));

        if (showTargetSlot)
        {
            SingleComposer
                .AddStaticText(targetHeader, CairoFont.WhiteMediumText(), ElementBounds.Fixed(30, 250, 700, 32))
                .AddItemSlotGrid(targetInventory, OnSlotModified, 1, ElementBounds.Fixed(30, 292, 55, 55), "targetslot")
                .AddStaticText(targetHint, CairoFont.WhiteSmallText(), ElementBounds.Fixed(105, 305, 590, 25));
        }
        else if (quest.TaskType == QuestTaskType.ReachPosition)
        {
            SingleComposer
                .AddStaticText($"Координаты: X {quest.TargetX:0.#}, Y {quest.TargetY:0.#}, Z {quest.TargetZ:0.#}", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 255, 540, 25))
                .AddStaticText($"Радиус: {quest.Radius} блоков", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 280, 260, 25));
        }

        bool showSubmitButton = quest.TaskType == QuestTaskType.SubmitItem && !state.ObjectiveReady && state.CanClaim;
        if (showSubmitButton)
        {
            SingleComposer
                .AddSmallButton("Сдать предмет", SubmitItem, ElementBounds.Fixed(30, 355, 155, 32));
        }

        int rewardsY = showSubmitButton ? 405 : 365;

        SingleComposer
            .AddStaticText("Награды", CairoFont.WhiteMediumText(), ElementBounds.Fixed(30, rewardsY, 220, 28))
            .AddItemSlotGrid(rewardsInventory, OnSlotModified, 3, ElementBounds.Fixed(30, rewardsY + 35, 190, 115), "rewardslots")
            .AddSmallButton(!state.CanClaim ? "На откате" : canComplete ? "Завершить" : "Не выполнено", ClaimReward, ElementBounds.Fixed(585, 555, 190, 35))
            .EndChildElements()
            .Compose();
    }

    private string GetTargetHeader()
    {
        if (quest.TaskType == QuestTaskType.KillEntity) return "Цель";

        if (quest.TaskType == QuestTaskType.CollectItem) return "Нужно иметь";
        if (quest.TaskType == QuestTaskType.SubmitItem) return "Нужно доставить";
        return "Цель";
    }

    private string GetTargetHint()
    {
        ItemStack? stack = quest.TargetStack?.ToItemStack(capi);
        string name = stack?.GetName() ?? "Не указано";
        string amount = quest.TargetAmount > 1 ? $" x{quest.TargetAmount}" : "";

        if (quest.TaskType == QuestTaskType.KillEntity) return name + amount;

        if (quest.TaskType == QuestTaskType.CollectItem || quest.TaskType == QuestTaskType.SubmitItem)
        {
            return name + amount;
        }

        return name + amount;
    }

    private void OnSlotModified(object obj) { }

    private bool SubmitItem()
    {
        if (quest.TaskType != QuestTaskType.SubmitItem) return true;

        capi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new SubmitQuestItemPacket
            {
                QuestId = quest.Id
            });

        TryClose();
        return true;
    }

    private bool ClaimReward()
    {
        if (!state.CanClaim) return true;

        if (!state.ObjectiveReady)
        {
            capi.TriggerIngameError(this, "notready", "Условие задания ещё не выполнено");
            return true;
        }

        capi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new ClaimQuestRewardPacket { QuestId = quest.Id });
        TryClose();
        return true;
    }

    private void OnTitleBarClose() => TryClose();

    private static string TaskTypeToRu(QuestTaskType type)
    {
        return type switch
        {
            QuestTaskType.CollectItem => "Иметь предмет в инвентаре",
            QuestTaskType.SubmitItem => "Доставить предмет",
            QuestTaskType.KillEntity => "Убить существо",
            QuestTaskType.ReachPosition => "Прийти в координаты",
            _ => "Текстовое задание"
        };
    }

    private static string FormatRemaining(long seconds)
    {
        System.TimeSpan time = System.TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours}ч {time.Minutes}м";
        if (time.TotalMinutes >= 1) return $"{time.Minutes}м {time.Seconds}с";
        return $"{time.Seconds}с";
    }
}
