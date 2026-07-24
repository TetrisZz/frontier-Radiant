using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI;

/// <summary>
/// Displays a compact radio-location marker with a hover tooltip.
/// </summary>
[UsedImplicitly]
public sealed class RadioCoordinatesTag : IMarkupTagHandler
{
    public string Name => "radiolocation";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text)
            || !node.Attributes.TryGetValue("tooltip", out var tooltip)
            || !tooltip.TryGetString(out var tooltipText))
        {
            control = null;
            return false;
        }

        control = new Label
        {
            Text = text,
            ToolTip = tooltipText,
            TooltipDelay = 0f,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
        };
        return true;
    }
}
