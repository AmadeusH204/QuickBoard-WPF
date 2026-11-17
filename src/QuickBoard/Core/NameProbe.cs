namespace QuickBoard.Core;

/// <summary>
/// 名称排版压力探针：用一组极端名称（单字符、纯中文、多个英文单词、无空格超长单词、中英混排）
/// 走真实磁贴模板渲染一张图，用来回归「短名称被二次折行 / 超长名称不带省略号被硬切」这两类问题。
/// 只读内存，不碰用户配置。
/// </summary>
public static class NameProbe
{
    private static readonly string[] Samples =
    {
        "QQ",
        "UU远程",
        "微信",
        "A",
        "Visual Studio Code",
        "Another Redis Desktop Manager",
        "SuperLongUnbreakableTokenWithoutSpaces",
        "Adobe Photoshop 2024 中文版",
        "PyCharm 2025.3.1",
        "三角洲行动",
        "Plain Craft Launcher 2.exe - 快捷方式",
        "Microsoft Office Word",
    };

    public static void Run(string outputPath)
    {
        var config = new BoardConfig();

        // 12 个键位（第一排 + 第二排前两格），样本全部覆盖
        var board = new AppGroup { Name = "名称排版测试" };
        for (int i = 0; i < Samples.Length; i++)
            board.Entries.Add(new AppEntry { Name = Samples[i], Slot = i + 1 });

        // 4 个「没有键位」的条目：走 WrapPanel 里的同一套磁贴模板，
        // 用来验证超出键位后磁贴宽度是否仍然统一（过去这里会随名称长度忽宽忽窄）
        board.Entries.Add(new AppEntry { Name = "Extra No Slot Long Name Sample", Slot = 0 });
        board.Entries.Add(new AppEntry { Name = "UU远程", Slot = 0 });
        board.Entries.Add(new AppEntry { Name = "Extra Greedy Tile Name 2024.1", Slot = 0 });
        board.Entries.Add(new AppEntry { Name = "QQ", Slot = 0 });

        // 收藏栏名称框更窄（68px），单列一组做对照
        var favorites = new AppGroup { Name = "收藏栏", IsFavorites = true };
        for (int i = 0; i < 12; i++)
            favorites.Entries.Add(new AppEntry { Name = Samples[i], FunctionKey = i + 1 });

        config.Groups.Add(board);
        config.Groups.Add(favorites);

        BoardShot.Run(new ConfigStore(), config, outputPath);
    }
}
