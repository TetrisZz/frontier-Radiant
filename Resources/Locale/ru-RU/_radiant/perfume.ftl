ent-BasePerfume = духи
    .desc = Маленький флакон с распылителем и нежным ароматом.
ent-PerfumeRose = духи "Роза"
    .desc = { ent-BasePerfume.desc }
ent-PerfumeLavender = духи "Лаванда"
    .desc = { ent-BasePerfume.desc }
ent-PerfumeCitrus = духи "Цитрус"
    .desc = { ent-BasePerfume.desc }
ent-PerfumePine = духи "Хвойный лес"
    .desc = { ent-BasePerfume.desc }
ent-PerfumeOcean = духи "Морская свежесть"
    .desc = { ent-BasePerfume.desc }
ent-PerfumeVanilla = духи "Ваниль"
    .desc = { ent-BasePerfume.desc }
ent-PerfumeMist = парфюмерная дымка

ent-PerfumeCherryLiqueur = духи "Пьяная вишня"
    .desc = Спелая вишня, горьковатый миндаль и тягучая ликёрная нота.
ent-PerfumeTobaccoVanilla = духи "Табак и ваниль"
    .desc = Тёплый табачный лист, мягкая ваниль и щепотка пряностей.
ent-PerfumePearSilk = духи "Грушевый шёлк"
    .desc = Сочная груша, белые цветы и мягкий мускусный шлейф.
ent-PerfumeWhiteTea = духи "Белый чай"
    .desc = Белый чай, искристый бергамот и сухая кедровая древесина.
ent-PerfumeMidnightCoffee = духи "Кофе после полуночи"
    .desc = Крепкий кофе, жасмин и тёплая ваниль в послевкусии.
ent-PerfumeSaltedSkin = духи "Солёная кожа"
    .desc = Морская соль, шалфей и прогретое солнцем дерево.

perfume-scent-cherry-liqueur = густой аромат спелой вишни, миндаля и ликёра
perfume-scent-tobacco-vanilla = тёплый аромат табачного листа, ванили и пряностей
perfume-scent-pear-silk = нежный аромат груши, белых цветов и мускуса
perfume-scent-white-tea = свежий аромат белого чая, бергамота и кедра
perfume-scent-midnight-coffee = тягучий аромат кофе, жасмина и ванили
perfume-scent-salted-skin = суховатый аромат морской соли, шалфея и тёплого дерева

perfume-scent-rose = нежный аромат розы
perfume-scent-lavender = мягкий аромат лаванды
perfume-scent-citrus = свежий цитрусовый аромат
perfume-scent-pine = прохладный аромат хвойного леса
perfume-scent-ocean = лёгкий аромат морского бриза
perfume-scent-vanilla = тёплый сладковатый аромат ванили
perfume-fragrance = Аромат: { $fragrance }.
perfume-uses = Осталось применений: { $uses }.
perfume-empty = Во флаконе больше не осталось духов.
perfume-invalid-target = Духи можно нанести на себя или находящегося рядом персонажа.
perfume-applied = Вы наносите духи. Теперь ощущается { $fragrance }.
perfume-smell = { GENDER($target) ->
    [female] От неё
    [epicene] От них
    *[other] От него
    } ощущается { $fragrance }.
perfume-sniff = Принюхаться
perfume-washed = Аромат духов смыт.
