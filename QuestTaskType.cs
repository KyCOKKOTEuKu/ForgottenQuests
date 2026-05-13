namespace ForgottenQuests;

public class QuestProgress
{
    public string QuestId { get; set; } = "";
    public int CurrentAmount { get; set; }
    public bool Completed { get; set; }
    public long LastCompletedUnixTime { get; set; }
}
