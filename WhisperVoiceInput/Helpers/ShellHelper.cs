using System;
using System.Text;

namespace WhisperVoiceInput.Helpers;

/// <summary>
/// Utilities for building and escaping shell commands used by the completion hook.
/// </summary>
public static class ShellHelper
{
    public const string ResultPlaceholder = "{{RESULT}}";

    /// <summary>
    /// Returns the system shell executable and its "run command" flag,
    /// auto-detected from environment variables.
    /// Linux/macOS: $SHELL (fallback /bin/sh), flag -c.
    /// Windows: COMSPEC (fallback cmd.exe), flag /c.
    /// </summary>
    public static (string Shell, string Flag) GetSystemShell()
    {
        if (OperatingSystem.IsWindows())
        {
            var comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            return (comspec, "/c");
        }

        var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
        return (shell, "-c");
    }

    /// <summary>
    /// POSIX single-quote escaping: wraps <paramref name="text"/> in single quotes,
    /// replacing each embedded <c>'</c> with <c>'\''</c>.
    /// </summary>
    public static string ShellEscape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "''";

        var sb = new StringBuilder(text.Length + 2);
        sb.Append('\'');
        foreach (var ch in text)
        {
            if (ch == '\'')
                sb.Append("'\\''");
            else
                sb.Append(ch);
        }
        sb.Append('\'');
        return sb.ToString();
    }

    /// <summary>
    /// Replaces every occurrence of <see cref="ResultPlaceholder"/> in
    /// <paramref name="commandTemplate"/> with the shell-escaped
    /// <paramref name="resultText"/>.
    /// </summary>
    public static string BuildHookCommand(string commandTemplate, string resultText)
    {
        if (string.IsNullOrEmpty(commandTemplate))
            return commandTemplate;

        return commandTemplate.Replace(ResultPlaceholder, ShellEscape(resultText));
    }
}
