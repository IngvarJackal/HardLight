using System.Diagnostics.CodeAnalysis;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

public sealed class FormTag : IMarkupTagHandler
{
    public string Name => "form";

    public static event Action<int, int>? OnFormClicked;

    public static bool Enabled { get; set; } = true;

    public static float FontLineHeight = 16.0f; // Default value, will be updated by PaperWindow

    /// <inheritdoc/>
    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        var underscores = 25;
        if (node.Value.TryGetLong(out var val))
        {
            underscores = (int) val;
        }

        var index = 0;
        if (node.Attributes.TryGetValue("i", out var indexVal) && indexVal.TryGetLong(out var i))
        {
            index = (int) i;
        }

        var text = new string('_', underscores);

        if (!Enabled)
        {
            control = new Label { Text = text };
            return true;
        }

        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterMode.Stop,
            FontColorOverride = Color.CornflowerBlue,
            DefaultCursorShape = Control.CursorShape.Hand,
            Margin = new Thickness(2, 0)
        };

        label.OnMouseEntered += _ => label.FontColorOverride = Color.LightSkyBlue;
        label.OnMouseExited += _ => label.FontColorOverride = Color.CornflowerBlue;

        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            OnFormClicked?.Invoke(index, underscores);
        };

        control = label;
        return true;
    }
}
