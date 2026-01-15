salvage-expedition-window-finish = Завершить экспедицию
salvage-expedition-announcement-early-finish = Экспедиция была окончена. Шаттл покинет планету через {$departTime} секунд.
salvage-expedition-announcement-destruction = { $count ->
    [1] Уничтожьте {$structure} до окончания экспедиции.
    *[others] Уничтожьте {$count} {MAKEPLURAL($structure)} до окончания экспедиции.
}
salvage-expedition-announcement-elimination = { $count ->
    [1] Ликвидируйте {$target} до окончания экспедиции.
    *[others] Ликвидируйте {$count} {MAKEPLURAL($target)} до окончания экспедиции.
}
salvage-expedition-announcement-destruction-entity-fallback = сооружение
salvage-expedition-announcement-elimination-entity-fallback = цель

salvage-expedition-shuttle-not-found = Не обнаружен шаттл.
salvage-expedition-not-everyone-aboard = Не вся команда на шаттле! {CAPITALIZE(THE($target))} всё еще отсутствует!
salvage-expedition-failed = Экспедиция провалена.

# Salvage mods
salvage-time-mod-standard-time = Нормальная продолжительность
salvage-time-mod-rush = Уменьшенная продолжительность

salvage-weather-mod-heavy-snowfall = Сильный снегопад
salvage-weather-mod-rain = Дождь

salvage-biome-mod-shadow = Теневой лес

salvage-dungeon-mod-cave-factory = Фабрика в пещере
salvage-dungeon-mod-med-sci = Научно-медицинская база
salvage-dungeon-mod-factory-dorms = Фабричное общежитие
salvage-dungeon-mod-lava-mercenary = База наемников над лавой
salvage-dungeon-mod-virology-lab = Вирусологическая лаборатория
salvage-dungeon-mod-salvage-outpost = Шахтерский форпост

salvage-air-mod-1 = 82 N2(Азот), 21 O2(Кислород)
salvage-air-mod-2 = 72 N2(Азот), 21 O2(Кислород), 10 N2O(Оксид азота)
salvage-air-mod-3 = 72 N2(Азот), 21 O2(Кислород), 10 H2O(Водяной пар)
salvage-air-mod-4 = 72 N2(Азот), 21 O2(Кислород), 10 NH3(Аммиак)
salvage-air-mod-5 = 72 N2(Азот), 21 O2(Кислород), 10 CO2(Диоксид углерода)
salvage-air-mod-6 = 79 N2(Азот), 21 O2(Кислород), 5 P(Плазма)
salvage-air-mod-7 = 57 N2(Азот), 21 O2(Кислород), 15 NH3(Аммиак), 5 P(Плазма), 5 N2O(Оксид азота)
salvage-air-mod-8 = 57 N2(Азот), 21 O2(Кислород), 15 H2O(Водяной пар), 5 NH3(Аммиак), 5 N2O(Оксид азота)
salvage-air-mod-9 = 57 N2(Азот), 21 O2(Кислород), 15 CO2(Диоксид углерода), 5 P(Плазма), 5 N2O(Оксид азота)
salvage-air-mod-10 = 82 CO2(Диоксид углерода), 21 O2(Кислород)
salvage-air-mod-11 = 67 CO2(Диоксид углерода), 31 O2(Кислород), 5 P(Плазма)
salvage-air-mod-12 = 103 H2O(Водяной пар)
salvage-air-mod-13 = 103 NH3(Аммиак)
salvage-air-mod-14 = 103 N2O(Оксид азота)
salvage-air-mod-15 = 103 CO2(Диоксид углерода)
salvage-air-mod-16 = 34 CO2(Диоксид углерода), 34 NH3(Аммиак), 34 N2O(Оксид азота)
salvage-air-mod-17 = 34 H2O(Водяной пар), 34 NH3(Аммиак), 34 N2O(Оксид азота)
salvage-air-mod-18 = 34 H2O(Водяной пар), 34 N2O(Оксид азота), 17 NH3(Аммиак), 17 CO2(Диоксид углерода)
salvage-air-mod-unknown = Неизвестная атмосфера

salvage-expedition-difficulty-NFModerate = Умеренная
salvage-expedition-difficulty-NFHazardous = Высокая
salvage-expedition-difficulty-NFExtreme = Экстремальная

salvage-expedition-megafauna-remaining =
    Осталась устранить { $count } { $count ->
        [one] цель.
        [few] цели.
       *[other] целей.
    }

salvage-expedition-type-Destruction = Разрушение
salvage-expedition-type-Elimination = Устранение