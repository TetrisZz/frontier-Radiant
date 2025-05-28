ban-manager-notify-discord-footer = - Round {$round}

ban-manager-notify-discord-format-days = { $days } { $days ->
        [one] день
        [few] дня
       *[other] дней
    }.

ban-manager-notify-discord-format-hours = { $hours } { $hours ->
        [one] час
        [few] часа
       *[other] часов
    }.

ban-manager-notify-discord-format-minutes = { $minutes } { $minutes ->
        [one] минута
        [few] минуты
       *[other] минут
    }.

ban-manager-notify-discord = 
    > **Временный бан**
    > **Нарушитель:** ``{$player}``
    > **Длительность:** {$time}

    > **По причине:** {$reason}
    > **Ответственный за наказание:** {$admin}

ban-manager-notify-discord-perma = 
    > **Перманентный бан**
    > **Нарушитель:** ``{$player}``

    > **По причине:** {$reason}
    > **Ответственный за наказание:** {$admin}

ban-manager-notify-discord-role = 
    > **Временный бан роли**
    > **Нарушитель:** ``{$player}``
    > **Длительность:** {$time}
    > **Роль:** {$role}

    > **По причине:** {$reason}
    > **Ответственный за наказание:** {$admin}

ban-manager-notify-discord-role-perma = 
    > **Перманентный бан роли**
    > **Нарушитель:** ``{$player}``
    > **Роль:** {$role}

    > **По причине:** {$reason}
    > **Ответственный за наказание:** {$admin}