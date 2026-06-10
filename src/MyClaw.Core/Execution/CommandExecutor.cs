using System.Diagnostics;
using System.Text;

namespace MyClaw.Core.Execution;

/// <summary>
/// 命令执行结果
/// </summary>
public class CommandResult
{
    /// <summary>
    /// 输出内容
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// 退出码
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess => ExitCode == 0;
}

/// <summary>
/// 安全命令执行器 - 白名单机制
/// </summary>
public class CommandExecutor
{
    // 允许的命令白名单
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // 文件操作
        "ls", "cat", "find", "grep", "head", "tail", "wc", "dir", "type",
        // Git 操作
        "git",
        // 环境检查
        "echo", "date", "uname", "which", "pwd", "ps", "whoami", "hostname",
        // 包管理
        "npm", "node", "pnpm", "yarn", "npx",
        "python", "python3", "pip", "pip3",
        "cargo", "rustc",
        "go", "golang",
        // 构建工具
        "make", "cmake", "msbuild",
        // 其他
        "tree", "du", "df", "curl", "wget"
    };

    // 危险命令黑名单（双重保险，按命令名精确匹配）
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "rd", "rmdir",
        "sudo", "su",
        "chown", "chmod", "chgrp",
        "mv", "move",
        "dd", "mkfs", "fdisk", "format",
        "shutdown", "reboot", "halt",
        "kill", "pkill", "killall"
    };

    // 危险 shell 元字符/模式：命令替换、管道、重定向、链式执行、后台执行、换行
    private static readonly string[] DangerousShellPatterns =
    {
        "$(", "${", "`",          // 命令/变量替换
        "|", "&", ";",            // 管道 / 后台 / 链式
        ">", "<",                 // 重定向（含 >>、2> 等）
        "\n", "\r"                // 多行注入
    };

    /// <summary>
    /// 执行命令（带安全检查）
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(string command, int timeoutMs = 10000)
    {
        // 安全检查
        var validation = ValidateCommand(command);
        if (!validation.IsValid)
        {
            return new CommandResult
            {
                Output = $"安全错误: {validation.ErrorMessage}",
                ExitCode = -1
            };
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShell(),
                Arguments = GetShellArguments(command),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() =>
                process.WaitForExit(timeoutMs));

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return new CommandResult
                {
                    Output = "错误: 命令超时 (10秒限制)",
                    ExitCode = -1
                };
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            // 限制输出大小 (1MB)
            const int maxOutput = 1024 * 1024;
            if (output.Length > maxOutput)
            {
                output = output.Substring(0, maxOutput) +
                    "\n... [输出已截断，超过 1MB 限制]";
            }

            return new CommandResult
            {
                Output = output + (string.IsNullOrEmpty(error) ? "" : $"\n[stderr]: {error}"),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Output = $"执行错误: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// 验证命令安全性。
    ///
    /// 先按字符检测危险的 shell 元字符（命令替换 <c>$()</c>/反引号、管道、重定向、
    /// 链式执行、换行），再按 <em>命令名 token</em>（而非子串）匹配黑/白名单，
    /// 从而既不会误杀含 "rm"/"dd" 等子串的合法命令（如 <c>grep firmware</c>、
    /// <c>cat addr.txt</c>），也不会漏掉 <c>$(...)</c>/反引号/单向重定向等注入。
    /// </summary>
    internal (bool IsValid, string ErrorMessage) ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (false, "命令为空");
        }

        // 1. 危险 shell 元字符检测（按字符/模式，杜绝命令替换、管道、重定向、链式执行）
        foreach (var pattern in DangerousShellPatterns)
        {
            if (command.Contains(pattern, StringComparison.Ordinal))
            {
                var shown = pattern.Replace("\n", "\\n").Replace("\r", "\\r");
                return (false, $"命令包含危险的 shell 元字符/模式: '{shown}'");
            }
        }

        // 2. 解析主命令名（剥离路径），按 token 精确匹配，避免子串误杀
        var firstToken = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        var cmdName = (firstToken.Contains('/') || firstToken.Contains('\\'))
            ? Path.GetFileName(firstToken)
            : firstToken;

        // 3. 黑名单（精确匹配命令名）
        if (BlockedCommands.Contains(cmdName))
        {
            return (false, $"命令 '{cmdName}' 在危险命令黑名单中");
        }

        // 4. 白名单
        if (!AllowedCommands.Contains(cmdName))
        {
            return (false, $"命令 '{cmdName}' 不在允许的白名单中");
        }

        return (true, string.Empty);
    }

    private string GetShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return "cmd.exe";
        }
        return "/bin/bash";
    }

    private string GetShellArguments(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"/c {command}";
        }
        return $"-c \"{command}\"";
    }
}
