cmd-whitelistadd-desc = Добавить игрока в вайтлист сервера.
cmd-whitelistadd-help = Использование: whitelistadd <username или  User ID>
cmd-whitelistadd-existing = { $username } уже находится в вайтлисте!
cmd-whitelistadd-added = { $username } добавлен в вайтлист
cmd-whitelistadd-not-found = Не удалось найти игрока '{ $username }'
cmd-whitelistadd-arg-player = [player]
cmd-whitelistremove-desc = Удалить игрока с вайтлиста сервера.
cmd-whitelistremove-help = Использование: whitelistremove <username или  User ID>
cmd-whitelistremove-existing = { $username } не находится в вайтлисте!
cmd-whitelistremove-removed = { $username } удалён с вайтлиста
cmd-whitelistremove-not-found = Не удалось найти игрока '{ $username }'
cmd-whitelistremove-arg-player = [player]
cmd-kicknonwhitelisted-desc = Кикнуть всег игроков не в белом списке с сервера.
cmd-kicknonwhitelisted-help = Использование: kicknonwhitelisted
ban-banned-permanent = Этот бан можно только обжаловать. Для этого посетите { $link }.
ban-banned-permanent-appeal = Этот бан можно только обжаловать. Для этого посетите { $link }.
ban-expires = Вы получили бан на { $duration } минут, и он истечёт { $time } по UTC (для московского времени добавьте 3 часа).
ban-banned-1 = Вам, или другому пользователю этого компьютера или соединения, запрещено здесь играть.
ban-banned-2 = Причина бана: "{ $reason }"
ban-banned-3 = Попытки обойти этот бан, например, путём создания нового аккаунта, будут фиксироваться.
soft-player-cap-full = Сервер заполнен!
panic-bunker-account-denied = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже
whitelist-playtime = У вас недостаточно игрового времени, чтобы присоединиться к этому серверу. Вам нужно не менее { $minutes } минут игрового времени, чтобы присоединиться к этому серверу.
whitelist-player-count = Этот сервер в настоящее время не принимает игроков. Пожалуйста, повторите попытку позже.
whitelist-notes = У вас слишком много заметок администратора, чтобы присоединиться к этому серверу. Вы можете проверить свои заметки, набрав /adminremarks в чате.
whitelist-manual = Вы не внесены в белый список на этом сервере.
whitelist-blacklisted = Вы занесены в черный список на этом сервере.
whitelist-always-deny = Вам запрещено присоединяться к этому серверу.
whitelist-fail-prefix = Не в белом списке: { $msg }
whitelist-misconfigured = Сервер неправильно настроен и не принимает игроков. Пожалуйста, свяжитесь с владельцем сервера и повторите попытку позже.
cmd-blacklistadd-desc = Добавляет игрока с указанным именем пользователя в черный список сервера.
cmd-blacklistadd-help = Используйте: blacklistadd <username>
cmd-blacklistadd-existing = { $username } уже в черном списке!
cmd-blacklistadd-added = { $username } добавлен в черный список
cmd-blacklistadd-not-found = Не удалось найти '{ $username }'
cmd-blacklistadd-arg-player = [player]
cmd-blacklistremove-desc = Удаляет игрока с указанным именем пользователя из черного списка сервера.
cmd-blacklistremove-help = Используйте: blacklistremove <username>
cmd-blacklistremove-existing = { $username } не в черном списке!
cmd-blacklistremove-removed = { $username } удален из черного списка
cmd-blacklistremove-not-found = Не удалось найти '{ $username }'
cmd-blacklistremove-arg-player = [player]
panic-bunker-account-denied-reason = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже Причина: "{ $reason }"
panic-bunker-account-reason-account = Ваш аккаунт Space Station 14 слишком новый. Он должен быть старше { $minutes } минут
panic-bunker-account-reason-overall =
    Необходимо минимальное отыгранное Вами время на сервере — { $minutes } { $minutes ->
        [one] минута
        [few] минуты
       *[other] минут
    }.
baby-jail-account-denied = Этот сервер - сервер для новичков, предназначенный для новых игроков и тех, кто хочет им помочь. Новые подключения слишком старых или не внесенных в белый список аккаунтов не принимаются. Загляните на другие серверы и посмотрите все, что может предложить Space Station 14. Веселитесь!
baby-jail-account-denied-reason = Этот сервер - сервер для новичков, предназначенный для новых игроков и тех, кто хочет им помочь. Новые подключения слишком старых или не внесенных в белый список аккаунтов не принимаются. Загляните на другие серверы и посмотрите все, что может предложить Space Station 14. Веселитесь! Причина: "{ $reason }"
baby-jail-account-reason-account = Ваша учетная запись Space Station 14 слишком старая. Он должен быть моложе, чем { $minutes } минут
baby-jail-account-reason-overall = Ваше общее игровое время на сервере должно быть меньше, чем { $minutes } $minutes
