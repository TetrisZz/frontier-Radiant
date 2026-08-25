using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using Content.Shared.Paper;
using static Content.Shared.Paper.PaperComponent;
using Content.Client._Goobstation.Languages; // Radiant Sector
using Content.Shared._Goobstation.Languages; // Radiant Sector
using Content.Shared.Ghost; // Radiant Sector
using Robust.Shared.Player; // Radiant Sector

namespace Content.Client.Paper.UI;

[UsedImplicitly]
public sealed partial class PaperBoundUserInterface : BoundUserInterface // DeltaV - made partial
{
    [ViewVariables]
    private PaperWindow? _window;
    private readonly LanguageMenuSystem _languageMenu;
    private readonly ISharedPlayerManager _playerManager;

    public PaperBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _languageMenu = EntMan.System<LanguageMenuSystem>();
        _playerManager = IoCManager.Resolve<ISharedPlayerManager>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PaperWindow>();
        _window.OnSaved += InputOnTextEntered;
        _window.Typing += OnTyping; // DeltaV
        _window.SubmitPressed += OnSubmit; // DeltaV
        _window.OnClose += OnSubmit; // DeltaV

        if (EntMan.TryGetComponent<PaperComponent>(Owner, out var paper))
        {
            _window.MaxInputLength = paper.ContentSize;
        }
        if (EntMan.TryGetComponent<PaperVisualsComponent>(Owner, out var visuals))
        {
            _window.InitVisuals(Owner, visuals);
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_window == null)
            return;

        var paperState = (PaperBoundUserInterfaceState) state;
        // Radiant Sector: a blank sheet has no meaningful language yet. Let the writer edit it,
        // then assign the currently selected language when the text is saved.
        if (string.IsNullOrWhiteSpace(paperState.Text) || UnderstandsLanguage(paperState.Language))
        {
            _window.Populate(paperState);
            return;
        }

        // Radiant Sector: strip formatting before substitution so unknown writing cannot smuggle readable markup.
        var plainText = FormattedMessage.RemoveMarkupPermissive(paperState.Text);
        var garbled = ObfuscateWriting(paperState.Language ?? "Общегалактический", plainText);
        _window.Populate(new PaperBoundUserInterfaceState(garbled, paperState.StampedBy, PaperAction.Read,
            Loc.GetString("paper-ui-language-unknown")));
    }

    private void InputOnTextEntered(string text)
    {
        var nativeSelected = _languageMenu.CachedState?.NativeSelected == true;
        if (_playerManager.LocalEntity is { } player && EntMan.HasComponent<NativeLanguageOnlyComponent>(player))
            nativeSelected = true;

        SendMessage(new PaperInputTextMessage(text, nativeSelected));

        if (_window != null)
        {
            _window.Input.TextRope = Rope.Leaf.Empty;
            _window.Input.CursorPosition = new TextEdit.CursorPos(0, TextEdit.LineBreakBias.Top);
        }
    }

    private bool UnderstandsLanguage(string? language)
    {
        if (_playerManager.LocalEntity is not { } reader)
            return false;

        if (EntMan.HasComponent<GhostComponent>(reader))
            return true;

        if (language == null)
            return !EntMan.HasComponent<NativeLanguageOnlyComponent>(reader);

        return !EntMan.HasComponent<NativeLanguageUnfamiliarComponent>(reader)
            && SpeciesLanguageUtility.GetNativeLanguage(EntMan, reader) == language;
    }

    private static string ObfuscateWriting(string language, string text)
    {
        // Radiant Sector: stable substitution keeps the same document visually consistent between openings.
        var fragments = language switch
        {
            "Бабблилиш" => new[] { "блюмп", "бульк", "бламп", "блимпаф" },
            "Сиик'тайр" => new[] { "мяу", "мяв", "мрау", "мурр" },
            "Синта'Унати" => new[] { "ссс", "щщх", "сщщ", "ххш" },
            "Моффик" => new[] { "seky", "møtmå", "köndag", "viiteh" },
            "Корневой язык" => new[] { "пфф", "пуф", "пфпуф", "паф" },
            "Счечи" => new[] { "чи", "ше", "крр", "пии" },
            "Нехина" => new[] { "бульк", "гррл", "хаар", "глур" },
            "Сумеречный" => new[] { "мрр", "вум", "ши", "нур" },
            _ => new[] { "эм", "ах", "тс", "мм" },
        };

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
        {
            var seed = index;
            foreach (var character in words[index])
                seed += character;
            words[index] = fragments[Math.Abs(seed) % fragments.Length];
        }

        return string.Join(' ', words);
    }
}
