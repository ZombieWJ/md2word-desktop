namespace Md2Word.Desktop;

internal static class AppBranding
{
    internal static Icon WindowIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream("Md2Word.AppIcon")!;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
