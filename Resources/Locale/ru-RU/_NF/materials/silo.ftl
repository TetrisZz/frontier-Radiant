ore-silo-ui-nf-itemlist-entry = {$linked ->
    [true] {"[Привязан] "}
    *[False] {""}
} {$name} {$inRange ->
    [true] {""}
    *[false] (Вне Диапазона)
}
