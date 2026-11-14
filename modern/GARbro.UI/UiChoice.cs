namespace GARbro.Desktop;

/// <summary>Stable setting values are independent of translated display labels.</summary>
public sealed record UiChoice(string Value, string Label)
{
    public override string ToString() => Label;
}
