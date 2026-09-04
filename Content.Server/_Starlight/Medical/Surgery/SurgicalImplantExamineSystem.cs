using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Radio.Components;
using Content.Shared.Radio;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Radiant sector: keeps externally visible augmentation clues out of the ordinary examine text
/// and exposes them through their own button next to health and stripping.
/// </summary>
public sealed class SurgicalImplantExamineSystem : EntitySystem
{
    private const string Icon = "/Textures/Interface/VerbIcons/information.svg.192dpi.png";

    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(
        EntityUid uid,
        HumanoidAppearanceComponent component,
        GetVerbsEvent<ExamineVerb> args)
    {
        var markup = CreateMarkup(uid, args.User);
        if (markup.IsEmpty)
            return;

        var detailsRange = _examine.IsInDetailsRange(args.User, uid);
        var verb = new ExamineVerb
        {
            Act = () => _examine.SendExamineTooltip(args.User, uid, CreateMarkup(uid, args.User), false, false),
            Text = Loc.GetString("starlight-surgery-augment-examine-verb"),
            Message = detailsRange
                ? Loc.GetString("starlight-surgery-augment-examine-hover")
                : Loc.GetString("starlight-surgery-augment-examine-disabled"),
            Category = VerbCategory.Examine,
            Disabled = !detailsRange,
            Icon = new SpriteSpecifier.Texture(new ResPath(Icon)),
        };

        args.Verbs.Add(verb);
    }

    private FormattedMessage CreateMarkup(EntityUid uid, EntityUid viewer)
    {
        var identity = Identity.Entity(uid, EntityManager);
        var entries = new List<(string Text, SurgicalAugmentVisibility Visibility)>();
        var shown = new HashSet<string>();
        var hasCommsImplant = false;

        foreach (var (organ, _) in _body.GetBodyOrgans(uid))
            AddAugment(organ);

        foreach (var (part, _) in _body.GetBodyChildren(uid))
            AddAugment(part);

        if (entries.Count == 0)
            return FormattedMessage.Empty;

        entries.Sort((a, b) => b.Visibility.CompareTo(a.Visibility));

        var message = new FormattedMessage();
        message.AddMarkupOrThrow($"[bold]{Loc.GetString("starlight-surgery-augment-examine-title")}[/bold]");

        foreach (var entry in entries)
        {
            message.PushNewline();
            var text = Loc.GetString(entry.Text, ("user", identity));
            message.AddMarkupOrThrow($"[color={GetVisibilityColor(entry.Visibility)}]{text}[/color]");
        }

        // Radiant sector: the implant itself can leave a visible hint, but the
        // actual loaded frequencies are private and should only be shown to the
        // person carrying the communications implant.
        if (hasCommsImplant && viewer == uid)
            AddRadioChannels(uid, message);

        return message;

        void AddAugment(EntityUid augment)
        {
            if (!TryComp<SurgicalImplantExamineComponent>(augment, out var examinable))
                return;

            var text = examinable.Text;
            var visibility = examinable.Visibility;
            if (MetaData(augment).EntityPrototype?.ID is { } prototypeId)
            {
                hasCommsImplant |= prototypeId == "BrainImplantComms";

                foreach (var (fragment, hint) in examinable.PrototypeHints)
                {
                    if (!prototypeId.Contains(fragment, StringComparison.Ordinal))
                        continue;

                    text = hint;
                    if (examinable.PrototypeVisibilities.TryGetValue(fragment, out var modelVisibility))
                        visibility = modelVisibility;
                    break;
                }
            }

            if (shown.Add(text))
                entries.Add((text, visibility));
        }
    }

    private void AddRadioChannels(EntityUid uid, FormattedMessage message)
    {
        message.PushNewline();
        message.AddMarkupOrThrow($"[color=#777781]{Loc.GetString("examine-encryption-channels-prefix")}[/color]");

        if (!TryComp<EncryptionKeyHolderComponent>(uid, out var holder) || holder.Channels.Count == 0)
        {
            message.PushNewline();
            message.AddMarkupOrThrow($"[color=#686872]{Loc.GetString("starlight-surgery-augment-radio-no-channels")}[/color]");
            return;
        }

        foreach (var channelId in holder.Channels)
        {
            if (!_prototype.TryIndex<RadioChannelPrototype>(channelId, out var channel))
                continue;

            var key = channelId == SharedChatSystem.CommonChannel
                ? SharedChatSystem.RadioCommonPrefix.ToString()
                : $"{SharedChatSystem.RadioChannelPrefix}{channel.KeyCode}";

            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString("examine-encryption-channel",
                ("color", channel.Color),
                ("key", key),
                ("id", channel.LocalizedName),
                ("freq", channel.Frequency / 10f)));
        }
    }

    private static string GetVisibilityColor(SurgicalAugmentVisibility visibility)
    {
        return visibility switch
        {
            SurgicalAugmentVisibility.Concealed => "#686872",
            SurgicalAugmentVisibility.Subtle => "#B8BBC7",
            SurgicalAugmentVisibility.Noticeable => "#67B7FF",
            SurgicalAugmentVisibility.Obvious => "#E5AD5C",
            SurgicalAugmentVisibility.Combat => "#FF6262",
            _ => "#B8BBC7",
        };
    }
}
