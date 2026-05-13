namespace ForgottenQuests;

public class QuestClientState
{
    public string QuestId { get; set; } = "";
    public bool CanClaim { get; set; }
    public bool ObjectiveReady { get; set; }
    public int ObjectiveProgress { get; set; }
    public long RemainingSeconds { get; set; }
    public long LastCompletedUnixTime { get; set; }
}
