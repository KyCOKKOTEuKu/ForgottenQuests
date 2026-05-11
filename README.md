# ForgottenQuests

Система квестов для Vintage Story.

## Изменения fixed5

- Настоящие предметные слоты через `InventoryGeneric` + `AddItemSlotGrid`.
- В редакторе квеста добавлена ячейка цели:
  - для задания `CollectItem` / `SubmitItem` туда кладётся нужный предмет;
  - для задания `KillEntity` туда кладётся предмет существа/его дроп как метка цели.
- Награды настраиваются сеткой 5x5, всего 25 слотов.
- Игрок может открыть квест, прочитать описание и посмотреть награды.
- Откат считается по реальному UTC-времени, а не по игровым часам.
- Сервер не выдаёт награду повторно, пока реальный cooldown не закончился.
- Если награда не помещается в инвентарь, остаток выбрасывается рядом с игроком.

## Зависимости

Проект настроен под установку игры:

```text
D:\Vintagestory
```



fixed19: kill quest detection uses DamageSource.GetCauseEntity(), SourceEntity/CauseEntity, non-standard reflection fields, and nearest-player fallback when no killer is reported. Entity IDs are normalized with game: prefix.
