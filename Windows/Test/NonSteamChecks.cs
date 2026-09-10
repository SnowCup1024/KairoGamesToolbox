using KairosoftGameToolbox.Services;

static class NonSteamChecks
{
    public static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "NonSteamTest-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "game/KairoGames_Data"));
        var game = Path.Combine(root, "game");
        var catalogPath = Path.Combine(root, "catalog.json");
        File.WriteAllText(catalogPath, """{"games":[{"appid":2934180,"name":"Doraemon Dorayaki Shop Story","name_zh_cn":"哆啦A梦的铜锣烧店物语"}]}""");
        var catalog = new AppIdCatalog(catalogPath);
        void Reject(string label, Action action)
        {
            try { action(); check(label, false); } catch (Exception ex) when (ex is IOException or InvalidDataException) { check(label, true); }
        }
        try
        {
            Reject("没有 EXE 拒绝添加", () => NonSteamLibraryService.Identify(game, catalog));
            File.WriteAllText(Path.Combine(game, "KairoGames.exe"), "synthetic");
            var info = Path.Combine(game, "KairoGames_Data/app.info");
            File.WriteAllText(info, "Kairosoft\nドラえもんのどら焼き屋さん物語");
            check("日文 app.info 自动识别游戏", NonSteamLibraryService.Identify(game, catalog).AppId == 2934180);
            var records = Path.Combine(root, "data/library.json");
            new NonSteamLibraryService(records).Save(2934180, game);
            check("首次添加保存后重新加载目录", new NonSteamLibraryService(records).Load().Single().Directory == game);
            new NonSteamLibraryService(records).Save(2934180, game);
            check("重复添加同一游戏不产生重复卡片", new NonSteamLibraryService(records).Load().Count == 1);
            File.WriteAllText(Path.Combine(game, "steam_appid.txt"), "999");
            Reject("身份与 AppID 冲突被拒绝", () => NonSteamLibraryService.Identify(game, catalog));
            File.Delete(Path.Combine(game, "steam_appid.txt"));
            File.WriteAllText(info, "OtherCompany\nDoraemon Dorayaki Shop Story");
            Reject("同名非开罗游戏被拒绝", () => NonSteamLibraryService.Identify(game, catalog));
            File.WriteAllText(info, "Kairosoft\nUnknownGame");
            Reject("无法确定身份时不猜测", () => NonSteamLibraryService.Identify(game, catalog));
        }
        finally { Directory.Delete(root, true); }
    }
}
