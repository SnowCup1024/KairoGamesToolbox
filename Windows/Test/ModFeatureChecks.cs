using KairosoftGameToolbox.Services;

static class ModFeatureChecks
{
    public static void Run(Action<string, bool> check)
    {
        check("十二个修改项宽屏排列为四列三行", ModFeatures.Columns(12, 1400) == 4);
        check("十二个修改项窄屏自动减少至两列", ModFeatures.Columns(12, 600) == 2);
        check("窄窗口至少保留一列", ModFeatures.Columns(12, 180) == 1);
        check("单项不会扩展成多个空列", ModFeatures.Columns(1, 1400) == 1);
        check("没有修改项时布局仍合法", ModFeatures.Columns(0, 0) == 1);
        check("未适配的游戏没有金钱反加", ModFeatures.ForGame(2191490).Count == 0);
        var root = Path.Combine(Path.GetTempPath(), "ModFeatureTest-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            check("没有游戏目录时禁止连接", !ModFeatures.CanConnect(2934180, null));
            File.WriteAllText(Path.Combine(root, "KairoGames.exe"), "synthetic");
            check("游戏已安装但没有模组时禁止连接", !ModFeatures.CanConnect(2934180, root));
            File.WriteAllText(Path.Combine(root, ".kairomods-install.json"), "{}");
            check("仅残留安装记录不能启用连接", !ModFeatures.CanConnect(2934180, root));
            var plugin = Path.Combine(root, "BepInEx/plugins/KairoMods.Observer/KairoMods.Observer.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
            File.WriteAllText(plugin, "synthetic");
            check("存在游戏与模组文件时允许尝试连接", ModFeatures.CanConnect(2934180, root));
            check("其他游戏不能误用当前模组控制", !ModFeatures.CanConnect(2191490, root));
            File.Delete(plugin);
            check("删除模组后再次禁止连接", !ModFeatures.CanConnect(2934180, root));
        }
        finally { Directory.Delete(root, true); }
    }
}
