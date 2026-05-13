using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ForgottenQuests;

public class GuiDialogQuestMenu : GuiDialog
{
    private List<QuestData> quests = new();
    private List<QuestClientState> states = new();
    private bool canEditQuests;

    public override string ToggleKeyCombinationCode => "forgottenquests-open";

    public GuiDialogQuestMenu(ICoreClientAPI capi) : base(capi)
    {
        capi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SetMessageHandler<QuestListPacket>(OnQuestList);

        ComposeDialog();
        RequestQuests();
    }

    private void RequestQuests()
    {
        capi.Network.GetChannel(ForgottenQuestsModSystem.ChannelName)
            .SendPacket(new RequestQuestListPacket());
    }

    private void OnQuestList(QuestListPacket packet)
    {
        quests = JsonUtil.FromString<List<QuestData>>(packet.QuestsJson) ?? new List<QuestData>();
        states = JsonUtil.FromString<List<QuestClientState>>(packet.StatesJson) ?? new List<QuestClientState>();
        canEditQuests = packet.CanEdit;
        ComposeDialog();
    }

    private QuestClientState StateFor(QuestData quest)
    {
        return states.FirstOrDefault(s => s.QuestId == quest.Id) ?? new QuestClientState { QuestId = quest.Id, CanClaim = true };
    }

    private void ComposeDialog()
    {
        ElementBounds bg = ElementBounds.Fixed(0, 0, 860, 620);
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

        bool canEdit = canEditQuests || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;

        SingleComposer = capi.Gui.CreateCompo("forgottenquests-menu", dialogBounds)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar("Задания", OnTitleBarClose)
            .BeginChildElements(bg);

        double y = 45;

        if (quests.Count == 0)
        {
            SingleComposer.AddStaticText("Заданий пока нет.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(30, y, 400, 30));
            y += 35;
        }

        foreach (QuestData quest in quests)
        {
            QuestClientState state = StateFor(quest);
            string cooldownText = state.CanClaim ? "Доступно" : "Откат: " + FormatRemaining(state.RemainingSeconds);
            bool ready = state.CanClaim && state.ObjectiveReady;

            SingleComposer.AddStaticText(quest.Title, CairoFont.WhiteMediumText(), ElementBounds.Fixed(30, y, 330, 32));
            SingleComposer.AddStaticText(cooldownText, CairoFont.WhiteSmallText(), ElementBounds.Fixed(370, y + 4, 155, 26));
            SingleComposer.AddSmallButton("Открыть", () => OpenDetails(quest), ElementBounds.Fixed(535, y, 95, 30));

            if (ready)
            {
                SingleComposer.AddSmallButton("Готово", () => true, ElementBounds.Fixed(640, y, 105, 30));
            }

            if (canEdit)
            {
                SingleComposer.AddSmallButton("Настроить", () => OpenEditor(quest), ElementBounds.Fixed(755, y, 95, 30));
            }

            y += 48;
        }

        if (canEdit)
        {
            SingleComposer.AddSmallButton("Создать задание", () => OpenEditor(new QuestData()), ElementBounds.Fixed(30, 565, 180, 32));
        }

        SingleComposer.EndChildElements().Compose();
    }

    private bool OpenDetails(QuestData quest)
    {
        new GuiDialogQuestDetails(capi, quest, StateFor(quest)).TryOpen();
        return true;
    }

    private bool OpenEditor(QuestData quest)
    {
        new GuiDialogQuestEditor(capi, quest).TryOpen();
        return true;
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private static string FormatRemaining(long seconds)
    {
        System.TimeSpan time = System.TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours}ч {time.Minutes}м";
        if (time.TotalMinutes >= 1) return $"{time.Minutes}м {time.Seconds}с";
        return $"{time.Seconds}с";
    }
}
