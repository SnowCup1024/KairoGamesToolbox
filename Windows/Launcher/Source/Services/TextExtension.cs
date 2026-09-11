using Microsoft.UI.Xaml.Markup;

namespace KairosoftGameToolbox.Services;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => L.T(Key);
}
