using System;

namespace ForgottenQuests;

public class QuestData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Новое задание";
    public string Description { get; set; } = "Описание задания";
    public QuestTaskType TaskType { get; set; } = QuestTaskType.CustomText;

    // Безопасное JSON-представление предмета-цели.
    // Для задания "принести" сюда кладётся нужный предмет.
    // Для задания "убить" сюда кладётся предмет-метка существа/дропа.
    public QuestItemStackData? TargetStack { get; set; }

    // Старое поле оставлено только для совместимости со старыми сохранёнными конфигами.
    public string KillEntityCode { get; set; } = "";

    public int TargetAmount { get; set; } = 1;

    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public int Radius { get; set; } = 3;

    // Реальные часы, не игровые.
    public int CooldownHours { get; set; } = 24;

    // 9 слотов наград, сетка 3x3. В JSON храним только код/тип/количество, не ItemStack целиком.
    public QuestItemStackData?[] RewardSlots { get; set; } = new QuestItemStackData?[9];
}
