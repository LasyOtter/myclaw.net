namespace MyClaw.Core.Configuration;

/// <summary>
/// 定位项目的 <c>templates</c> 目录。
///
/// 查找顺序：环境变量 <c>MYCLAW_TEMPLATES_DIR</c> → 从当前工作目录向上 5 层 →
/// 可执行文件目录 → 用户配置目录。找不到返回 <c>null</c>。
/// </summary>
public static class TemplateLocator
{
    /// <summary>
    /// 返回找到的 templates 目录绝对路径；找不到返回 <c>null</c>。
    /// </summary>
    public static string? Find()
    {
        // 1. 环境变量
        var envTemplates = Environment.GetEnvironmentVariable("MYCLAW_TEMPLATES_DIR");
        if (!string.IsNullOrEmpty(envTemplates) && Directory.Exists(envTemplates))
        {
            return envTemplates;
        }

        // 2. 从当前工作目录向上查找
        var searchDir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            var templatesPath = Path.Combine(searchDir, "templates");
            if (Directory.Exists(templatesPath) &&
                (File.Exists(Path.Combine(templatesPath, "AGENTS.md")) ||
                 File.Exists(Path.Combine(templatesPath, "SOUL.md"))))
            {
                return templatesPath;
            }

            var parentDir = Directory.GetParent(searchDir);
            if (parentDir == null) break;
            searchDir = parentDir.FullName;
        }

        // 3. 可执行文件所在目录
        var exeTemplatesPath = Path.Combine(AppContext.BaseDirectory, "templates");
        if (Directory.Exists(exeTemplatesPath))
        {
            return exeTemplatesPath;
        }

        // 4. 用户配置目录
        var userTemplatesPath = Path.Combine(ConfigurationLoader.ConfigDir, "templates");
        if (Directory.Exists(userTemplatesPath))
        {
            return userTemplatesPath;
        }

        return null;
    }
}
