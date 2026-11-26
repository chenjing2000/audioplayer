namespace AudioPlayer.Script;

public sealed record ScriptAppearance
{
    internal static readonly string[] Fonts = ["Newsreader", "Source Serif 4", "Georgia", "Microsoft YaHei UI", "Nunito"];
    internal static readonly int[] Sizes = [17, 18, 19, 20];
    internal static readonly double[] Heights = [1.5, 1.6, 1.7, 1.8];
    internal static readonly int[] Spacings = [8, 12, 16, 20];
    internal static readonly int[] Indents = [0, 1, 2];
    internal static readonly string[] Colors = ["#283747", "#344054", "#3F4750", "#315247", "#513D4D"];
    internal static readonly string[] PlaybackColors = ["#F6EDC2", "#F5E4D6", "#E1EEDB", "#E0ECF4", "#ECE6F3"];
    internal static readonly string[] Backgrounds = ["#F8F8F6", "#F0F3F7", "#F7F1E6", "#EFF5F0", "#F1F0F7"];

    public string Font { get; init; } = "Newsreader";
    public int FontSize { get; init; } = 17;
    public double LineHeight { get; init; } = 1.6;
    public int ParagraphAfter { get; init; } = 16;
    public int FirstLineIndent { get; init; } = 2;
    public string Color { get; init; } = "#283747";
    public string ScriptColor { get; init; } = "#F6EDC2";
    public string Background { get; init; } = "#F8F8F6";

    // Only formerly supported values are normalized on load. Unknown values
    // still fail validation, and loading never writes back to the user's file.
    internal ScriptAppearance NormalizeRemovedOptions() => this with
    {
        Font = Font is "Segoe UI" or "Arial" or "Times New Roman" ? "Newsreader" : Font,
        FontSize = FontSize switch { 14 or 15 or 16 => 17, 22 or 24 or 26 or 28 => 20, _ => FontSize },
        LineHeight = LineHeight switch { 1.4 => 1.5, 2.0 => 1.8, _ => LineHeight },
        ParagraphAfter = ParagraphAfter switch { 0 => 8, 14 => 16, 18 or 24 => 20, _ => ParagraphAfter },
        FirstLineIndent = FirstLineIndent == 3 ? 2 : FirstLineIndent,
        ScriptColor = ScriptColor switch
        {
            "#A33D30" or "#B64550" or "#D71920" or "#F5E7A6" => "#F6EDC2",
            "#A45A2C" or "#BC4600" or "#F4D9C4" => "#F5E4D6",
            "#856300" or "#8A2BE2" or "#E5DDF2" => "#ECE6F3",
            "#53713C" or "#007D45" or "#D5E8CC" => "#E1EEDB",
            "#26798A" or "#1685B8" or "#D3E5F3" => "#E0ECF4",
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
