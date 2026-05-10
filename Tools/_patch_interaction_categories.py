# Проставляет поле category: всем прототипам Interaction* в interactions.yml
# (по смыслу кнопок из ru-RU interactionbuttonmassage.ftl).
import re
from pathlib import Path

PATH = Path(__file__).resolve().parents[1] / "Resources/Prototypes/_radiant/interactions.yml"

# Ровно 100 ключей — все Interaction* из файла
ID_TO_CAT: dict[str, str] = {
    "InteractionHandshake": "body",
    "InteractionAirKiss": "lips",
    "InteractionGiveFive": "body",
    "InteractionNod": "head",
    "InteractionPatted": "body",
    "InteractionWink": "head",
    "InteractionBow": "body",
    "InteractionTickles": "body",
    "InteractionStrokeHead": "head",
    "InteractionSlapFace": "head",
    "InteractionSqueezeCheeks": "head",
    "InteractionMassageShoulder": "body",
    "InteractionHug": "body",
    "InteractionCaress": "head",
    "InteractionKissForehead": "head",
    "InteractionHoldHand": "body",
    "InteractionStrokeHair": "head",
    "InteractionKissCheek": "head",
    "InteractionHoldFace": "head",
    "InteractionStrokeBack": "body",
    "InteractionKissNeck": "head",
    "InteractionHoldTight": "body",
    "InteractionKissLips": "lips",
    "InteractionintImateTouch": "body",
    "InteractionTouchBreasts": "chest",
    "InteractionTouchAss": "groin",
    "InteractionBrushLips": "lips",
    "InteractionintBiteLips": "lips",
    "InteractionPullClose": "body",
    "InteractionSlowCaress": "body",
    "InteractionSlap": "groin",
    "InteractionPullHair": "head",
    "InteractionAnalEnterSlowly": "groin",
    "InteractionAnalTraxSlowly": "groin",
    "InteractionVagTraxSlowly": "groin",
    "InteractionAnalEnterFaster": "groin",
    "InteractionVaginaEnterSlowly": "groin",
    "InteractionVaginaEnterFaster": "groin",
    "InteractionTouchDick": "groin",
    "InteractionLickDick": "groin",
    "InteractionKissDick": "groin",
    "InteractionTermDick": "groin",
    "InteractionMasturbateDick": "groin",
    "InteractionSuckDick": "groin",
    "InteractionLegkDick": "groin",
    "InteractionSlowThrust": "groin",
    "InteractionFastThrust": "groin",
    "InteractionDeepThrust": "groin",
    "InteractionLickAnal": "groin",
    "InteractionRubbingDickVagina": "groin",
    "InteractionRubbingVaginaDick": "groin",
    "InteractionRubbingVaginaVagina": "groin",
    "InteractionMinetTraxSlowly": "groin",
    "InteractionSisTraxSlowly": "groin",
    "InteractionRubbingVaginaHand": "body",
    "InteractionRubbingVaginaLeg": "body",
    "InteractionCunnilingusSlowly": "groin",
    "InteractionCunnilingusFaster": "groin",
    "InteractionClitoris": "groin",
    "InteractionMassageVagina": "groin",
    "InteractionFaceBreasts": "chest",
    "InteractionLickBreasts": "chest",
    "InteractionTraxBreasts": "chest",
    "InteractionBiteBreasts": "chest",
    "InteractionTouchNippels": "chest",
    "InteractionLickNippels": "chest",
    "InteractionRoughGrip": "body",
    "InteractionChokeLightly": "head",
    "InteractionBiteRough": "body",
    "InteractionScratchBack": "body",
    "InteractionBiteLip": "lips",
    "InteractionDionaHug": "body",
    "InteractionDionaTouchFoliage": "body",
    "InteractionVoxTouchWings": "body",
    "InteractionVoxEnterSlowly": "groin",
    "InteractionVoxEnterFaster": "groin",
    "InteractionRubbingVaginaVox": "groin",
    "InteractionTouchFilament": "body",
    "InteractionEnterSlowlyFilament": "groin",
    "InteractionEnterFasterFilament": "groin",
    "InteractionVulpkaninStroking": "body",
    "InteractionVulpkaninStrokingBack": "body",
    "InteractionTouchTail": "tail",
    "InteractionGrabTail": "tail",
    "InteractionTugsTail": "tail",
    "InteractionMassageTail": "tail",
    "InteractionWavingTail": "tail",
    "InteractionLickTail": "tail",
    "InteractionEatTail": "tail",
    "InteractionHoldsDickTail": "tail",
    "InteractionRubbingVaginaTail": "tail",
    "InteractionInsertTailVox": "tail",
    "InteractionInsertTailAnal": "tail",
    "InteractionInsertTailVagina": "tail",
    "InteractionRunsTailVagina": "tail",
    "InteractionMassageTailDick": "tail",
    "InteractionHugsTailDick": "tail",
    "InteractionHugsTaillDick": "tail",
    "InteractionHugsSlimeDick": "tail",
    "InteractionFondleSlimeDick": "tail",
    "InteractionTicklesEars": "head",
    "InteractionKissEars": "head",
    "InteractionLickEars": "head",
    "InteractionBiteEars": "head",
    "InteractionStraponEnterAnalSlowly": "groin",
    "InteractionStraponEnterAnalFaster": "groin",
    "InteractionStraponEnterVaginaSlowly": "groin",
    "InteractionStraponEnterVaginaFaster": "groin",
    "InteractionStraponEnterVoxSlowly": "groin",
    "InteractionStraponEnterVoxFaster": "groin",
}


def main() -> None:
    raw = PATH.read_text(encoding="utf-8")
    starts = [m.start() for m in re.finditer(r"(?m)^- type: interaction\s*$", raw)]
    out: list[str] = []
    last = 0
    for i, start in enumerate(starts):
        end = starts[i + 1] if i + 1 < len(starts) else len(raw)
        out.append(raw[last:start])
        block = raw[start:end]
        last = end

        lines = block.splitlines(keepends=True)
        if not lines:
            continue
        text = "".join(lines)
        if re.search(r"(?m)^  abstract:\s*true\s*$", text):
            out.append(text)
            continue

        m_id = re.search(r"(?m)^  id:\s*(Interaction\S+)\s*$", text)
        if not m_id:
            out.append(text)
            continue

        pid = m_id.group(1)
        cat = ID_TO_CAT.get(pid)
        if cat is None:
            raise SystemExit(f"Нет категории в словаре для: {pid}")

        lines = [l for l in lines if not re.match(r"^  category:\s*\S+\s*$", l)]

        new_lines: list[str] = []
        inserted = False
        for line in lines:
            new_lines.append(line)
            if not inserted and re.match(rf"^  id:\s*{re.escape(pid)}\s*$", line):
                new_lines.append(f"  category: {cat}\n")
                inserted = True
        if not inserted:
            raise SystemExit(f"Не удалось вставить category для {pid}")
        out.append("".join(new_lines))

    out.append(raw[last:])
    PATH.write_text("".join(out), encoding="utf-8")

    # Проверка: все Interaction* из файла есть в словаре
    all_ids = set(re.findall(r"(?m)^  id:\s*(Interaction\S+)\s*$", raw))
    missing = all_ids - set(ID_TO_CAT)
    extra = set(ID_TO_CAT) - all_ids
    if missing:
        raise SystemExit(f"В YAML есть id без словаря: {sorted(missing)}")
    if extra:
        raise SystemExit(f"В словаре лишние id: {sorted(extra)}")


if __name__ == "__main__":
    main()
