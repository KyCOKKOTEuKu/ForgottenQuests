using ProtoBuf;

namespace ForgottenQuests;

[ProtoContract]
public class RequestQuestListPacket
{
    [ProtoMember(1)]
    public int RequestId { get; set; } = 1;
}

[ProtoContract]
public class QuestListPacket
{
    [ProtoMember(1)]
    public string QuestsJson { get; set; } = "[]";

    [ProtoMember(2)]
    public string StatesJson { get; set; } = "[]";

    [ProtoMember(3)]
    public bool CanEdit { get; set; }
}

[ProtoContract]
public class SaveQuestPacket
{
    [ProtoMember(1)]
    public string Json { get; set; } = "";
}

[ProtoContract]
public class ClaimQuestRewardPacket
{
    [ProtoMember(1)]
    public string QuestId { get; set; } = "";
}

[ProtoContract]
public class SubmitQuestItemPacket
{
    [ProtoMember(1)]
    public string QuestId { get; set; } = "";

    [ProtoMember(2)]
    public string SubmittedStackJson { get; set; } = "";
}

[ProtoContract]
public class QuestCompletedClientPacket
{
    [ProtoMember(1)]
    public string QuestTitle { get; set; } = "";
}
