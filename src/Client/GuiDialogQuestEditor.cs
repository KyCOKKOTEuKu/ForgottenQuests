using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForgottenQuests;

public class GuiDialogQuestEditor : GuiDialog
{
    private readonly QuestData quest;
    private readonly InventoryGeneric targetInventory;
    private readonly InventoryGeneric rewardsInventory;

    public override string ToggleKeyCombinationCode => null;

    public GuiDialogQuestEditor(ICoreClientAPI capi, QuestData quest) : base(capi)
    {
        this.quest = quest;

        targetInventory = new InventoryGeneric(1, "forgottenquests-target-" + quest.Id, capi);
        rewardsInventory = new InventoryGeneric(9, "forgottenquests-rewards-" + quest.Id, capi);

        targetInventory[0].Itemstack = quest.TargetStack?.ToItemStack(capi);
        targetInventory[0].MarkDirty();
        FillInventory(rewardsInventory, quest.RewardSlots);

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
        ElementBounds bg = ElementBounds.Fixed(0, 0, 900, 680);
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

        SingleComposer = capi.Gui.CreateCompo("forgottenquests-editor-" + quest.Id, dialogBounds)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar("Настройка задания", OnTitleBarClose)
            .BeginChildElements(bg)

            .AddStaticText("Название", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 45, 160, 25))
            .AddTextInput(ElementBounds.Fixed(190, 42, 660, 30), text => quest.Title = text, CairoFont.WhiteSmallText(), "title")

            .AddStaticText("Описание", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 85, 160, 25))
            .AddTextInput(ElementBounds.Fixed(190, 82, 660, 30), text => quest.Description = text, CairoFont.WhiteSmallText(), "description")

            .AddStaticText("Тип задания", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 125, 160, 25))
            .AddDropDown(Enum.GetNames(typeof(QuestTaskType)), Enum.GetNames(typeof(QuestTaskType)), (int)quest.TaskType, OnTaskTypeChanged, ElementBounds.Fixed(190, 122, 250, 30), "tasktype")

            .AddStaticText("Цель", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 175, 160, 25))
            .AddStaticText("Для CollectItem/SubmitItem положи предмет. Для KillEntity положи предмет/метку существа.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(190, 166, 660, 45))
            .AddItemSlotGrid(targetInventory, OnTargetSlotModified, 1, ElementBounds.Fixed(190, 220, 55, 55), "targetslot")

            .AddStaticText("Количество", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 305, 160, 25))
            .AddNumberInput(ElementBounds.Fixed(190, 302, 95, 30), text => quest.TargetAmount = ParseInt(text, 1), CairoFont.WhiteSmallText(), "targetamount")

            .AddStaticText("Координаты X Y Z", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 355, 160, 25))
            .AddTextInput(ElementBounds.Fixed(190, 352, 90, 30), text => quest.TargetX = ParseDouble(text), CairoFont.WhiteSmallText(), "x")
            .AddTextInput(ElementBounds.Fixed(290, 352, 90, 30), text => quest.TargetY = ParseDouble(text), CairoFont.WhiteSmallText(), "y")
            .AddTextInput(ElementBounds.Fixed(390, 352, 90, 30), text => quest.TargetZ = ParseDouble(text), CairoFont.WhiteSmallText(), "z")

            .AddStaticText("Радиус", CairoFont.WhiteSmallText(), ElementBounds.Fixed(520, 355, 100, 25))
            .AddNumberInput(ElementBounds.Fixed(625, 352, 95, 30), text => quest.Radius = ParseInt(text, 3), CairoFont.WhiteSmallText(), "radius")

            .AddStaticText("Откат, реальных часов", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, 405, 180, 25))
            .AddNumberInput(ElementBounds.Fixed(230, 402, 95, 30), text => quest.CooldownHours = ParseInt(text, 24), CairoFont.WhiteSmallText(), "cooldown")

            .AddStaticText("Перетащи предметы в эти ячейки. При выполнении они будут выданы игроку.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(190, 468, 660, 25))
            .AddItemSlotGrid(rewardsInventory, OnRewardSlotModified, 3, ElementBounds.Fixed(30, 490, 190, 170), "rewardslots")

            .AddSmallButton("Сохранить", SaveQuest, ElementBounds.Fixed(710, 625, 140, 35))
            .EndChildElements()
            .Compose();

        SingleComposer.GetTextInput("title").SetValue(quest.Title);
        SingleComposer.GetTextInput("description").SetValue(quest.Description);
        SingleComposer.GetTextInput("targetamount").SetValue(quest.TargetAmount.ToString());
        SingleComposer.GetTextInput("x").SetValue(quest.TargetX.ToString());
        SingleComposer.GetTextInput("y").SetValue(quest.TargetY.ToString());
        SingleComposer.GetTextInput("z").SetValue(quest.TargetZ.ToString());
        SingleComposer.GetTextInput("radius").SetValue(quest.Radius.ToString());
        SingleComposer.GetTextInput("cooldown").SetValue(quest.CooldownHours.ToString());
    }

    private void OnTaskTypeChanged(string code, bool selected)
    {
        if (!selected) return;
        if (Enum.TryParse(code, out QuestTaskType type)) quest.TaskType = type;
    }

    private void OnTargetSlotModified(object obj)
    {
        // Важно: не пересобирать GUI во время изменения слота.
        // В VS 1.22 это может тихо закрывать клиент без crash-log, особенно при drag-and-drop.
        quest.TargetStack = QuestItemStackData.FromItemStack(targetInventory[0].Itemstack);
    }

    private void OnRewardSlotModified(object obj)
    {
        CopyRewardSlotsToQuest();
    }

    private void CopyRewardSlotsToQuest()
    {
        quest.RewardSlots = new QuestItemStackData?[9];
        for (int i = 0; i < rewardsInventory.Count && i < quest.RewardSlots.Length; i++)
        {
            quest.RewardSlots[i] = QuestItemStackData.FromItemStack(rewardsInventory[i].Itemstack);
        }
    }

    private bool SaveQuest()
    {
        quest.TargetStack = QuestItemStackData.FromItemStack(targetInventory[0].Itemstack);
        CopyRewardSlotsToQuest();

        string json = JsonUtil.ToString(quest);
        capi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new SaveQuestPacket { Json = json });
        TryClose();
        return true;
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out int value) ? value : fallback;
    }

    private static double ParseDouble(string text)
    {
        return double.TryParse(text, out double value) ? value : 0;
    }
}
