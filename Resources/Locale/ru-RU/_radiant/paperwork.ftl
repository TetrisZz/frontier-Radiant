ent-Photocopier = ксерокс
    .desc = Печатает стандартные бланки и копирует бумажные документы. Заправляется стопками бумаги и тонером.
ent-PrinterToner = картридж с тонером
    .desc = Картридж для заправки ксерокса.
photocopier-title = Ксерокс - документы
photocopier-search = Поиск бланка…
photocopier-print = Напечатать бланк
photocopier-copy = Скопировать оригинал
photocopier-copies = Экземпляров:
photocopier-original = Оригинал документа
paper-form-writing-required = Держите ручку в руке. Документ должен быть доступен для записи.
photocopier-copy-name = Копия: { $name }
photocopier-no-supplies = Недостаточно бумаги или тонера.
photocopier-body-print = Отпечатать задницу
photocopier-body-start = Вы устраиваетесь на стекле ксерокса. Не двигайтесь...
photocopier-body-finish = Ксерокс выдаёт весьма личный отпечаток.
photocopier-body-caption = Отпечатано на ксероксе. Не самый обычный рабочий документ.
photocopier-body-no-power = Ксерокс не подключён к питанию.
photocopier-body-busy = Ксерокс ещё печатает.
photocopier-body-original = Сначала извлеките оригинал из ксерокса.
photocopier-body-unreachable = Вы не можете воспользоваться ксероксом отсюда.
ent-PaperBodyPrint = отпечаток задницы
    .desc = Чёрно-белый отпечаток. Кого-то явно нельзя оставлять наедине с оргтехникой.
ent-PaperBodyPrintMale = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintFemale = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintLizard = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintCat = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintFluffy = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintSlime = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
ent-PaperBodyPrintMachine = { ent-PaperBodyPrint }
    .desc = { ent-PaperBodyPrint.desc }
photocopier-template-error = Не удалось загрузить бланк. Печать отменена; бумага и тонер не потрачены.
photocopier-supplies = Бумага: { $paper } листов • Тонер: { $ink } страниц
photocopier-document-header =
    { document-divider }
    { "[head=2]" }{ $title }[/head]
    { document-divider }
    Дата печати: { $date }
    Время от начала смены: { $time }
    Станция / шаттл: { $ship }
# Шапки адаптированы из https://radiantsector.fandom.com/ru/wiki/Бумажная_Работа?oldid=1312
document-divider = ═════════════════════════════
document-stamp-area =
    { document-divider }
    { "[italic]" }Место для подписей и печатей[/italic]
document-research-base-unknown = МНБ [form]
document-letterhead-frontier =
    { "[mono][color=#1b487e]" }████░░░░████[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░████░[/color]  [/mono][head=3]Бланк документа[/head]
    { "[mono][color=#1b487e]" }████░░████░░[/color][/mono]
    { "[mono][color=#1b487e]" }████░████░░░[/color]  [/mono][bold]Шаттл { $ship }[/bold]
    { "[mono][color=#1b487e]" }████░░████░░[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░████░[/color][/mono]
    { "[mono][color=#1b487e]" }████░░░░████[/color][/mono]
document-letterhead-security =
    { "[mono][color=#003153]" }████░░░░████[/color][/mono]
    { "[mono][color=#003153]" }████░░░████░[/color]  [/mono][head=3]Бланк документа[/head]
    { "[mono][color=#003153]" }████░░████░░[/color][/mono]
    { "[mono][color=#003153]" }████░████░░░[/color]  [/mono][head=3]Форпост ОСК[/head]
    { "[mono][color=#003153]" }████░░████░░[/color][/mono]
    { "[mono][color=#003153]" }████░░░████░[/color][/mono]
    { "[mono][color=#003153]" }████░░░░████[/color][/mono]
document-letterhead-acorp =
    { "[mono][color=black]" }██████░░░░░██████[/color][/mono]
    { "[mono][color=black]" }█████░░███░░█████[/color]  [/mono][head=3]Бланк документа[/head]
    { "[mono][color=black]" }█████░░░░░░░█████[/color]  [/mono][bold]{ $base }[/bold]
    { "[mono][color=black]" }█████░░███░░█████[/color]  [/mono][head=3]Аколит Корпорейшн[/head]
    { "[mono][color=black]" }█████░░███░░█████[/color][/mono]
document-letterhead-separatists =
    { "[mono][color=#6e3b3b]" }█████░░░░░[/color][/mono]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color]  [/mono][head=3]Бланк документа[/head]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color][/mono]
    { "[mono][color=#6e3b3b]" }██░░░░░░░░[/color]  [/mono][head=3]Картель Сепаратистов[/head]
    { "[mono][color=#6e3b3b]" }█████░░░░░[/color][/mono]
document-letterhead-medical =
    { "[mono][color=#28716b]" }░░░████░░░[/color][/mono]
    { "[mono][color=#28716b]" }░░░████░░░[/color]  [/mono][head=3]Медицинская служба[/head]
    { "[mono][color=#28716b]" }██████████[/color]  [/mono][bold]Фронтир[/bold]
    { "[mono][color=#28716b]" }░░░████░░░[/color]  [/mono]Медицинская документация
    { "[mono][color=#28716b]" }░░░████░░░[/color][/mono]

document-category-frontier = Фронтир - общие документы
document-category-security = Форпост ОСК - ДВБ и КФ
document-category-acorp = Аколит Корпорейшн
document-category-separatists = Картель Сепаратистов
document-category-medical = Медицинские документы
document-form-general = Стандартный бланк документа
document-form-salary = Запрос заработной платы
document-form-mercenary = Лицензия наёмника
document-form-arrest = Ордер на арест
document-form-search = Ордер на обыск
document-form-fine = Уведомление о штрафе
document-form-acorp-general = Акорп - стандартный бланк
document-form-acorp-work = Акорп - отчёт о работе
document-form-acorp-artifact = Акорп - исследование артефакта
document-form-acorp-anomaly = Акорп - аномальная активность
document-form-separatist-sale = Договор купли-продажи раба
document-form-separatist-lease = Договор аренды раба
document-form-crew = Договор найма в экипаж
document-form-expedition = Отчёт об экспедиции
document-form-handover = Акт приёма-передачи груза
document-form-medical-consent = Согласие на медицинское вмешательство
document-content-general =
    Составитель документа: [form]
    Должность составителя: [form]
    Наименование документа: [form]

    Содержание документа:
    { "[form]" }

    Подписи и печати:
document-content-salary =
    Ф.И.О. сотрудника: [form]
    Должность: [form]
    Время начала работы: [form]
    Продолжительность работы: [form]
    Выполненные обязанности: [form]
    Запрашиваемая сумма: [form]

    Подпись заявителя: [form]
    Решение уполномоченного лица: [form]
    Место для подписей и печатей:
document-content-mercenary =
    Ф.И.О.: [form]
    Должность / организация: [form]
    Причина получения: [form]
    Предметы, которыми разрешено пользоваться: [form]
    Срок действия лицензии: [form]
    Уполномоченное лицо: [form]

    Бланк сам по себе не предоставляет разрешений. Требуется оформление уполномоченным должностным лицом.
    Подписи и печати:
document-content-arrest =
    { "[head=3]" }Обвиняемый[/head]
    Обвиняемый: [form]
    Описание: [form]

    { "[head=3]" }Сведения о правонарушении[/head]
    Номер дела: [form]
    Время и место правонарушения: [form]
    Обвинения: [form]

    { "[head=3]" }Информация о заявителе[/head]
    Заявитель: [form]
    Уполномоченный сотрудник: [form]

    Ордер действителен после одобрения, подписания и заверения уполномоченным должностным лицом согласно действующим законам сектора.
    Подписи и печати:
document-content-search =
    Составитель документа: [form]
    Должность: [form]
    Лицо / объект обыска: [form]
    Капитан и обозначение шаттла: [form]
    Основание обыска: [form]
    Границы разрешённого обыска: [form]

    Недействительно без подписи или печати уполномоченного сотрудника ДВБ/КФ либо Представителя Сектора.
    Подписи и печати:
document-content-fine =
    Составитель и должность: [form]
    Номер шаттла: [form]
    Ф.И.О. капитана / нарушителя: [form]
    Основание штрафа: [form]
    Сумма: [form]
    Место оплаты: Форпост ОСК
    Отметка об оплате: [form]

    Недействительно без оформления уполномоченным должностным лицом.
    Подписи и печати:
document-content-acorp-general =
    Составитель документа: [form]
    Должность: [form]
    Наименование документа: [form]
    Содержание:
    { "[form]" }

    Подписи и печати:
document-content-acorp-work =
    Составитель документа: [form]
    Полное наименование должности: [form]
    Время заступления на смену: [form]
    Время начала работы: [form]
    Получатель - Комендант Экспедиций / Директор Исследований: [form]

    Отчёт о выполнении указаний и обязанностей:
    { "[form]" }

    Подписи и печати:
document-content-acorp-artifact =
    Составитель и должность: [form]
    Получатель: [form]

    { "[head=3]" }Результаты исследования[/head]
    Внешний вид артефакта: [form]
    Количество исследованных узлов: [form]
    Стимуляторы: [form]
    Реакции: [form]
    Особые свойства: [form]
    Меры безопасности и рекомендации: [form]

    Подписи и печати:
document-content-acorp-anomaly =
    Время происшествия: [form]
    Место обнаружения: [form]
    Ответственный и должность: [form]
    Тип аномалии: [form]
    Уровень угрозы: [form]

    { "[head=3]" }Описание и принятые меры[/head]
    Описание события: [form]
    Принятые меры: [form]
    Статус инцидента: [form]
    Дополнительные замечания: [form]

    Подписи и печати:
document-content-separatist-sale =
    { "[head=3]" }Раб[/head]
    Имя передаваемого лица: [form]
    Описание: [form]
    Состояние: [form]

    { "[head=3]" }Сделка[/head]
    Продавец: Картель Сепаратистов
    Покупатель: [form]
    Сумма и валюта: [form]
    Условия передачи: [form]

    Фракционный документ Картеля. Не является разрешением властей Фронтира.
    Подписи сторон и печати:
document-content-separatist-lease =
    { "[head=3]" }Раб[/head]
    Имя передаваемого лица: [form]
    Описание и состояние: [form]

    { "[head=3]" }Условия аренды[/head]
    Арендодатель: Картель Сепаратистов
    Арендатор: [form]
    Срок аренды: [form]
    Цель и условия: [form]
    Сумма и валюта: [form]
    Условия возврата и компенсации: [form]

    Фракционный документ Картеля. Не является разрешением властей Фронтира.
    Подписи сторон и печати:
document-content-crew =
    Капитан и обозначение шаттла: [form]
    Нанимаемый сотрудник: [form]
    Должность и обязанности: [form]
    Срок работы: [form]
    Оплата и порядок расчёта: [form]
    Условия прекращения договора: [form]

    Подписи капитана и сотрудника:
document-content-expedition =
    Руководитель и состав экспедиции: [form]
    Шаттл: [form]
    Место назначения: [form]
    Время отправления / возвращения: [form]
    Поставленные задачи: [form]
    Результаты и обнаруженные объекты: [form]
    Опасности, потери и происшествия: [form]
    Приложения и переданные образцы: [form]

    Подписи и печати:
document-content-handover =
    Передающая сторона: [form]
    Принимающая сторона: [form]
    Шаттлы / место передачи: [form]
    Наименование и количество груза: [form]
    Состояние и упаковка: [form]
    Стоимость / основание передачи: [form]
    Замечания сторон: [form]

    Подписи передающей и принимающей сторон:
document-content-medical-consent =
    Пациент: [form]
    Лечащий специалист: [form]
    Предлагаемое вмешательство: [form]
    Разъяснённые риски и альтернативы: [form]
    Особые ограничения пациента: [form]
    Решение пациента: [form]

    Заполнение бланка не заменяет фактического согласия пациента.
    Подпись пациента: [form]
    Подпись специалиста: [form]

document-form-medical-examination = Медицинское заключение
document-form-medical-referral = Направление на обследование
document-form-medical-surgery = Протокол операции
document-form-medical-discharge = Выписка из медицинской карты
document-form-medical-prescription = Назначение лечения
document-form-medical-death = Медицинское свидетельство о смерти
document-content-medical-examination =
    { "[head=3]" }Сведения о пациенте[/head]
    Ф.И.О. и вид: [form]
    Лечащий специалист: [form]
    Время осмотра: [form]

    { "[head=3]" }Результаты осмотра[/head]
    Жалобы и анамнез: [form]
    Объективное состояние и обследования: [form]
    Заключение: [form]
    Ограничения и рекомендации: [form]
    Подпись специалиста: [form]
document-content-medical-referral =
    Пациент: [form]
    Направляющий специалист: [form]
    Медицинское подразделение / специалист: [form]
    Срочность: [form]

    { "[head=3]" }Основание направления[/head]
    Жалобы и предварительное заключение: [form]
    Требуемые обследования / консультация: [form]
    Противопоказания и особые условия: [form]
    Подпись специалиста: [form]
document-content-medical-surgery =
    Пациент: [form]
    Оперирующий специалист и ассистенты: [form]
    Основание вмешательства: [form]
    Согласие пациента / основание экстренного вмешательства: [form]
    Время начала и окончания: [form]

    { "[head=3]" }Ход операции[/head]
    Вмешательство и обезболивание: [form]
    Импланты / удалённые ткани: [form]
    Осложнения и принятые меры: [form]
    Результат и состояние пациента: [form]
    Послеоперационные назначения: [form]
    Подпись специалиста: [form]
document-content-medical-discharge =
    Пациент: [form]
    Время поступления и выписки: [form]
    Лечащий специалист: [form]
    Причина обращения / заключение: [form]

    { "[head=3]" }Итоги лечения[/head]
    Проведённые обследования и вмешательства: [form]
    Состояние при выписке: [form]
    Дальнейшее лечение и ограничения: [form]
    Контрольный осмотр: [form]
    Подпись специалиста: [form]
document-content-medical-prescription =
    Пациент: [form]
    Назначивший специалист: [form]
    Основание назначения: [form]

    { "[head=3]" }План лечения[/head]
    Препарат / процедура: [form]
    Дозировка и способ применения: [form]
    Частота и длительность: [form]
    Ограничения и противопоказания: [form]
    Контроль состояния и повторный осмотр: [form]
    Назначение действительно после подписи медицинского специалиста.
    Подпись специалиста: [form]
document-content-medical-death =
    Ф.И.О. умершего и вид: [form]
    Способ установления личности: [form]
    Дата, время и место обнаружения: [form]
    Время констатации смерти: [form]

    { "[head=3]" }Медицинское заключение[/head]
    Признаки и предполагаемая причина смерти: [form]
    Реанимационные мероприятия и результат: [form]
    Возможность дальнейшего восстановления: [form]
    Передача тела и личных вещей: [form]
    Констатировавший специалист и подпись: [form]
    Документ фиксирует медицинские обстоятельства и не заменяет расследование.
