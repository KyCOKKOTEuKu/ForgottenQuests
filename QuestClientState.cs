using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ForgottenQuests;

public class ForgottenQuestsModSystem : ModSystem
{
    public const string ChannelName = "forgottenquests";
    public const string QuestGiverPrivilege = "QuestGiver";

    public static ICoreClientAPI? ClientApi { get; private set; }
    public static ICoreServerAPI? ServerApi { get; private set; }
    public static QuestServerManager? ServerManager { get; private set; }

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<RequestQuestListPacket>()
            .RegisterMessageType<QuestListPacket>()
            .RegisterMessageType<SaveQuestPacket>()
            .RegisterMessageType<DeleteQuestPacket>()
            .RegisterMessageType<ClaimQuestRewardPacket>()
            .RegisterMessageType<SubmitQuestItemPacket>()
            .RegisterMessageType<QuestCompletedClientPacket>();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        ClientApi = api;

        api.Input.RegisterHotKey(
            "forgottenquestsplayermenu",
            "ForgottenQuests",
            GlKeys.V,
            HotkeyType.GUIOrOtherControls
        );

        api.Input.SetHotKeyHandler("forgottenquestsplayermenu", _ =>
        {
            new GuiDialogQuestMenu(api).TryOpen();
            return true;
        });

        api.Network.GetChannel(ChannelName)
            .SetMessageHandler<QuestCompletedClientPacket>(OnQuestCompletedClient);
    }

    private void OnQuestCompletedClient(QuestCompletedClientPacket packet)
    {
        if (ClientApi == null) return;

        string key = GetQuestMenuHotkeyText(ClientApi);
        string title = EscapeChatText(packet.QuestTitle);
        ClientApi.ShowChatMessage($"Квест {title} <font color=\"#84ff84\">выполнен</font>. Нажмите {key}, чтобы открыть меню заданий.");
    }

    private static string GetQuestMenuHotkeyText(ICoreClientAPI api)
    {
        HotKey? hotkey = api.Input.GetHotKeyByCode("forgottenquestsplayermenu");
        KeyCombination? mapping = hotkey?.CurrentMapping;
        if (mapping == null) return "V";

        string text = mapping.ToString();
        if (string.IsNullOrWhiteSpace(text)) text = mapping.PrimaryAsString();
        return string.IsNullOrWhiteSpace(text) ? "V" : text.ToUpperInvariant();
    }

    private static string EscapeChatText(string value)
    {
        return (value ?? "")
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerApi = api;
        api.Permissions.RegisterPrivilege(QuestGiverPrivilege, "Can create and edit ForgottenQuests quests");
        ServerManager = new QuestServerManager(api);
    }
}
