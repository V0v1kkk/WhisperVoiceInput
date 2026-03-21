using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Helpers;

namespace WhisperVoiceInput.Tests.Helpers;

[TestFixture]
public class ShellHelperTests
{
    #region ShellEscape

    [Test]
    public void ShellEscape_PlainText_WrapsInSingleQuotes()
    {
        ShellHelper.ShellEscape("hello").Should().Be("'hello'");
    }

    [Test]
    public void ShellEscape_TextWithSingleQuotes_EscapesProperly()
    {
        ShellHelper.ShellEscape("it's").Should().Be("'it'\\''s'");
    }

    [Test]
    public void ShellEscape_EmptyString_ReturnsEmptyQuotes()
    {
        ShellHelper.ShellEscape("").Should().Be("''");
    }

    [Test]
    public void ShellEscape_Null_ReturnsEmptyQuotes()
    {
        ShellHelper.ShellEscape(null!).Should().Be("''");
    }

    [Test]
    public void ShellEscape_SpecialShellChars_ArePreserved()
    {
        var input = "price is $100 & `echo hack` \\n \"quoted\" !bang";
        var escaped = ShellHelper.ShellEscape(input);
        escaped.Should().StartWith("'").And.EndWith("'");
        escaped.Should().Contain("$100");
        escaped.Should().Contain("`echo hack`");
        escaped.Should().Contain("\\n");
        escaped.Should().Contain("\"quoted\"");
        escaped.Should().Contain("!bang");
    }

    [Test]
    public void ShellEscape_Newlines_ArePreserved()
    {
        var input = "line1\nline2\nline3";
        var escaped = ShellHelper.ShellEscape(input);
        escaped.Should().Be("'line1\nline2\nline3'");
    }

    [Test]
    public void ShellEscape_MultipleSingleQuotes_AllEscaped()
    {
        ShellHelper.ShellEscape("a'b'c").Should().Be("'a'\\''b'\\''c'");
    }

    #endregion

    #region BuildHookCommand

    [Test]
    public void BuildHookCommand_NoPlaceholder_ReturnsCommandAsIs()
    {
        var cmd = "notify-send 'done'";
        ShellHelper.BuildHookCommand(cmd, "some text").Should().Be(cmd);
    }

    [Test]
    public void BuildHookCommand_WithPlaceholder_SubstitutesEscapedResult()
    {
        var cmd = "echo {{RESULT}}";
        var result = ShellHelper.BuildHookCommand(cmd, "hello world");
        result.Should().Be("echo 'hello world'");
    }

    [Test]
    public void BuildHookCommand_MultiplePlaceholders_SubstitutesAll()
    {
        var cmd = "echo {{RESULT}} > /tmp/out && cat {{RESULT}}";
        var result = ShellHelper.BuildHookCommand(cmd, "test");
        result.Should().Be("echo 'test' > /tmp/out && cat 'test'");
    }

    [Test]
    public void BuildHookCommand_PlaceholderWithSpecialChars_EscapesProperly()
    {
        var cmd = "echo {{RESULT}}";
        var result = ShellHelper.BuildHookCommand(cmd, "it's a $test");
        result.Should().Be("echo 'it'\\''s a $test'");
    }

    [Test]
    public void BuildHookCommand_EmptyTemplate_ReturnsEmpty()
    {
        ShellHelper.BuildHookCommand("", "text").Should().Be("");
    }

    [Test]
    public void BuildHookCommand_NullTemplate_ReturnsNull()
    {
        ShellHelper.BuildHookCommand(null!, "text").Should().BeNull();
    }

    #endregion

    #region GetSystemShell

    [Test]
    public void GetSystemShell_ReturnsNonEmptyShellAndFlag()
    {
        var (shell, flag) = ShellHelper.GetSystemShell();
        shell.Should().NotBeNullOrWhiteSpace();
        flag.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
