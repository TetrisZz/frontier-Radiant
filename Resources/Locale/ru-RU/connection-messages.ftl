cmd-whitelistadd-desc = Добавить игрока в вайтлист сервера.
cmd-whitelistadd-help = Использование: whitelistadd <username или  User ID>
cmd-whitelistadd-existing = {$username} уже находится в вайтлисте!
cmd-whitelistadd-added = {$username} добавлен в вайтлист
cmd-whitelistadd-not-found = Не удалось найти игрока '{$username}'
cmd-whitelistadd-arg-player = [player]

cmd-whitelistremove-desc = Удалить игрока с вайтлиста сервера.
cmd-whitelistremove-help = Использование: whitelistremove <username или User ID>
cmd-whitelistremove-existing = {$username} не находится в вайтлисте!
cmd-whitelistremove-removed = {$username} удалён из вайтлиста
cmd-whitelistremove-not-found =  Не удалось найти игрока '{$username}'
cmd-whitelistremove-arg-player = [player]

cmd-kicknonwhitelisted-desc = Кикнуть всех игроков не в белом списке с сервера.
cmd-kicknonwhitelisted-help = Использование: kicknonwhitelisted

ban-banned-permanent = Этот бан можно только обжаловать. Для этого посетите: https://discord.gg/radiant-sector
ban-banned-permanent-appeal = Этот бан можно только обжаловать. Для этого посетите: https://discord.gg/radiant-sector
ban-expires = Вы получили бан на {$duration} минут, он окончиться {$time} по UTC (для московского времени добавьте 3 часа).
ban-banned-1 = Вам, или другому пользователю этого компьютера или соединения, запрещено здесь играть.
ban-banned-2 = Причина бана: "{$reason}"
ban-banned-3 = Если эта блокировка была выдана по ошибке или мера слишком сурова по вашему мнению, вы можете подать апелляцию в discord, в соответствующем канале.
ban-banned-4 = Попытки обойти этот бан, например, путём создания нового аккаунта, будут фиксироваться.

soft-player-cap-full = Сервер заполнен!
panic-bunker-account-denied = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже
panic-bunker-account-denied-reason = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже. Причина: "{$reason}"
panic-bunker-account-reason-account = Ваш аккаунт Space Station 14 слишком новый. Он должен быть старше {$minutes} минут.
panic-bunker-account-reason-overall =
    Необходимо минимальное отыгранное Вами время на сервере — { $minutes } { $minutes ->
        [one] минута
        [few] минуты
       *[other] минут
    }.

whitelist-playtime = У вас недостаточно игрового времени, чтобы присоединиться к этому серверу. Чтобы присоединиться к этому серверу, вам потребуется как минимум {$minutes} минут игрового времени.
whitelist-player-count = В данный момент этот сервер не принимает игроков. Пожалуйста, повторите попытку позже.
whitelist-notes = На данный момент у вас слишком много заметок от администраторов, чтобы присоединиться к этому серверу. Вы можете проверить свои заметки, введя /adminremarks в чат.
whitelist-manual = Вы не внесены в белый список на этом сервере.
whitelist-blacklisted = Вы занесены в черный список на этом сервере.
whitelist-always-deny = Вам не разрешено подключаться к этому серверу.
whitelist-fail-prefix = Не внесен в белый список: {$msg}

cmd-blacklistadd-desc = Добавляет игрока с указанным именем пользователя в черный список сервера.
cmd-blacklistadd-help = Используйте: blacklistadd <username>
cmd-blacklistadd-existing = {$username} уже внесен в черный список!
cmd-blacklistadd-added = {$username} добавлен в черный список
cmd-blacklistadd-not-found = Не удалось найти игрока '{$username}'
cmd-blacklistadd-arg-player = [player]

cmd-blacklistremove-desc = Удаляет игрока с указанным именем пользователя из черного списка сервера.
cmd-blacklistremove-help = Используйте: blacklistremove <username>
cmd-blacklistremove-existing = {$username} нет в черном списке!
cmd-blacklistremove-removed = {$username} удален из черного списка
cmd-blacklistremove-not-found = Не удалось найти игрока '{$username}'
cmd-blacklistremove-arg-player = [player]

baby-jail-account-denied = Этот сервер предназначен для новичков и тех, кто хочет им помочь. Новые подключения со слишком старыми учетными записями или аккаунтами, которых нет в белом списке, не принимаются. Посетите другие серверы и узнайте все, что может предложить Space Station 14. Развлекайся!
baby-jail-account-denied-reason = Этот сервер предназначен для новичков и тех, кто хочет им помочь. Новые подключения со слишком старыми учетными записями или аккаунтами, которых нет в белом списке, не принимаются. Посетите другие серверы и узнайте все, что может предложить Space Station 14. Развлекайся! Причина: "{$reason}"
baby-jail-account-reason-account = Ваш аккаунт Space Station 14 слишком старый. Он должен быть моложе {$minutes} минут.
baby-jail-account-reason-overall =
    Ваше время отыгранное на сервере должно быть меньше — { $minutes } { $minutes ->
        [one] минута
        [few] минуты
       *[other] минут
    }.

generic-misconfigured = Сервер неправильно настроен и не принимает игроков. Пожалуйста, свяжитесь с владельцем сервера и повторите попытку позже.

ipintel-server-ratelimited = Этот сервер использует систему безопасности с внешней проверкой, которая достигла максимального предела проверки. Пожалуйста, обратитесь за помощью к администрации сервера и повторите попытку позже.
ipintel-unknown = Этот сервер использует систему безопасности с внешней проверкой, но на нем произошла ошибка. Пожалуйста, обратитесь за помощью к администрации сервера и повторите попытку позже.
ipintel-suspicious = Похоже, вы подключаетесь через центр обработки данных или VPN. По административным причинам мы не разрешаем использовать VPN-соединения. Пожалуйста, обратитесь за помощью к администрации сервера, если вы считаете, что это ложное сообщение.
