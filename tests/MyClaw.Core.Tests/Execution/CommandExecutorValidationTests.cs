using MyClaw.Core.Execution;
using Xunit;

namespace MyClaw.Core.Tests.Execution;

/// <summary>
/// <see cref="CommandExecutor.ValidateCommand"/> 的安全校验测试。
/// 直接测试纯函数式的校验逻辑，不实际派生进程。
/// </summary>
public class CommandExecutorValidationTests
{
    private readonly CommandExecutor _executor = new();

    private bool IsValid(string command) => _executor.ValidateCommand(command).IsValid;

    // ---- 合法命令应通过（含修复后不再误杀的子串情形）----

    [Theory]
    [InlineData("ls -la")]
    [InlineData("git status")]
    [InlineData("echo hello")]
    [InlineData("grep firmware ./src")]   // 含 "rm" 子串，旧实现会误杀
    [InlineData("cat addr.txt")]          // 含 "dd" 子串，旧实现会误杀
    [InlineData("find . -name format")]   // 含 "format" 子串，旧实现会误杀
    [InlineData("/usr/bin/git log")]      // 带路径的白名单命令
    public void ValidCommands_Pass(string command)
    {
        Assert.True(IsValid(command), $"应允许: {command}");
    }

    // ---- 黑名单命令应被拒绝 ----

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("sudo apt install x")]
    [InlineData("mv a b")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    [InlineData("reboot")]
    [InlineData("/bin/rm file")]          // 带路径的黑名单命令
    public void BlockedCommands_Rejected(string command)
    {
        Assert.False(IsValid(command), $"应拒绝: {command}");
    }

    // ---- 非白名单命令应被拒绝 ----

    [Theory]
    [InlineData("nmap localhost")]
    [InlineData("telnet host")]
    public void NonWhitelistedCommands_Rejected(string command)
    {
        Assert.False(IsValid(command));
    }

    // ---- shell 元字符注入应被拒绝（含旧实现漏网的情形）----

    [Theory]
    [InlineData("echo $(rm -rf /)")]      // 命令替换
    [InlineData("echo `rm -rf /`")]       // 反引号替换（旧实现漏网）
    [InlineData("echo ${HOME}")]          // 变量替换
    [InlineData("cat a | grep b")]        // 管道
    [InlineData("ls && rm x")]            // 链式
    [InlineData("ls; rm x")]              // 分号链式
    [InlineData("echo x > /etc/passwd")]  // 单向重定向（旧实现漏网）
    [InlineData("echo x >> /etc/passwd")] // 追加重定向
    [InlineData("cat < /etc/passwd")]     // 输入重定向
    [InlineData("ls &")]                  // 后台执行
    [InlineData("echo a\nrm -rf /")]      // 换行注入（旧实现漏网）
    public void ShellMetacharacters_Rejected(string command)
    {
        Assert.False(IsValid(command), $"应拒绝(含元字符): {command}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCommand_Rejected(string command)
    {
        Assert.False(IsValid(command));
    }
}
