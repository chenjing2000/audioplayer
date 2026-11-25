namespace AudioPlayer.Script;

public sealed record ScriptAppearance
{
    internal static readonly string[] Fonts = ["Georgia", "Microsoft YaHei UI", "Nunito", "Times New Roman", "Arial"];
    internal static readonly int[] Sizes = [17, 18, 19, 20];
    internal static readonly double[] Heights = [1.5, 1.6, 1.7, 1.8];
    internal static readonly int[] Spacings = [8, 12, 16, 20];
    internal static readonly int[] Indents = [0, 1, 2];
    internal static readonly string[] Colors = ["#283747", "#344054", "#3F4750", "#315247", "#513D4D"];
    internal static readonly string[] PlaybackColors = ["#F5E7A6", "#F4D9C4", "#D5E8CC", "#D3E5F3", "#E5DDF2"];
    internal static readonly string[] Backgrounds = ["#F8F8F6", "#F0F3F7", "#F7F1E6", "#EFF5F0", "#F1F0F7"];

    public string Font { get; init; } = "Georgia";
    public int FontSize { get; init; } = 17;
    public double LineHeight { get; init; } = 1.6;
    public int ParagraphAfter { get; init; } = 16;
    public int FirstLineIndent { get; init; } = 2;
    public string Color { get; init; } = "#283747";
    public string ScriptColor { get; init; } = "#F5E7A6";
    public string Background { get; init; } = "#F8F8F6";

    // Only formerly supported values are normalized on load. Unknown values
    // still fail validation, and loading never writes back to the user's file.
    internal ScriptAppearance NormalizeRemovedOptions() => this with
    {
        Font = Font == "Segoe UI" ? "Arial" : Font,
        FontSize = FontSize switch { 14 or 15 or 16 => 17, 22 or 24 or 26 or 28 => 20, _ => FontSize },
        LineHeight = LineHeight switch { 1.4 => 1.5, 2.0 => 1.8, _ => LineHeight },
        ParagraphAfter = ParagraphAfter switch { 0 => 8, 14 => 16, 18 or 24 => 20, _ => ParagraphAfter },
        FirstLineIndent = FirstLineIndent == 3 ? 2 : FirstLineIndent,
        ScriptColor = ScriptColor switch
        {
            "#A33D30" or "#B64550" or "#D71920" => "#F5E7A6",
            "#A45A2C" or "#BC4600" => "#F4D9C4",
            "#856300" or "#8A2BE2" => "#E5DDF2",
            "#53713C" or "#007D45" => "#D5E8CC",
            "#26798A" or "#1685B8" => "#D3E5F3",
            _ => ScriptColor
        },
        Background = Background == "#FFFFFF" ? "#F8F8F6" : Background
    };

    internal void Validate()
    {
        if (!Fonts.Contains(Font) || !Sizes.Contains(FontSize) || !Heights.Contains(LineHeight) ||
            !Spacings.Contains(ParagraphAfter) || !Indents.Contains(FirstLineIndent) ||
            !Colors.Contains(Color) || !PlaybackColors.Contains(ScriptColor) || !Backgrounds.Contains(Background))
            throw new ArgumentOutOfRangeException(nameof(ScriptAppearance), "无效的原文显示参数。");
    }
}
