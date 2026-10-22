using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Paperwork;

/// <summary>
/// Keeps form entry inside the document's rich-text layout.
/// </summary>
public sealed class PaperFormField : BoxContainer
{
    public readonly PaperFormButton Button;
    public readonly LineEdit Editor;
    private Action<string>? _submit;
    private bool _closing;

    public PaperFormField(float lineHeight)
    {
        Button = new PaperFormButton(lineHeight) { Disabled = true };
        Editor = new LineEdit
        {
            Name = "PaperFormInput",
            StyleClasses = { "PaperFormInput" },
            Visible = false,
            SetWidth = 180,
            SetHeight = Button.MinHeight,
        };
        AddChild(Button);
        AddChild(Editor);
        Editor.OnTextEntered += args =>
        {
            var submit = _submit;
            CancelEdit();
            submit?.Invoke(args.Text);
        };
        Editor.OnFocusExit += _ => CancelEdit();
    }

    public void BeginEdit(Action<string> submit)
    {
        _submit = submit;
        Button.Visible = false;
        Editor.Visible = true;
        Editor.Text = string.Empty;
        Editor.GrabKeyboardFocus();
    }

    public void CancelEdit()
    {
        if (_closing)
            return;

        _closing = true;
        try
        {
            _submit = null;
            // Hiding the focused control releases focus and invokes OnFocusExit again.
            Editor.Visible = false;
            Button.Visible = true;
        }
        finally
        {
            _closing = false;
        }
    }
}
