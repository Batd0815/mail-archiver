using MailArchiver.Services;

namespace MailArchiver.Tests;

public sealed class ThunderbirdRuleFileParserTests
{
    [Fact]
    public void Parses_supported_incoming_move_rules_and_decodes_folder_names()
    {
        const string file = """
            version="9"
            logging="no"
            name="1&1"
            enabled="yes"
            type="17"
            action="Move to folder"
            actionValue="mailbox://nobody@Local%20Folders/Kolpingstra%C3%9Fe"
            condition="AND (from,contains,1&1)"
            name="News"
            enabled="no"
            type="17"
            action="Move to folder"
            actionValue="mailbox://nobody@Local%20Folders/Newsletter"
            condition="AND (subject,contains,News)"
            """;

        var result = ThunderbirdRuleFileParser.Parse(file);

        Assert.Equal(2, result.SupportedRules.Count);
        Assert.Empty(result.UnsupportedRules);
        Assert.Equal("Kolpingstraße", result.SupportedRules[0].TargetFolder);
        Assert.Equal("1&1", result.SupportedRules[0].FromContains);
        Assert.True(result.SupportedRules[0].SourceEnabled);
        Assert.Equal("News", result.SupportedRules[1].SubjectContains);
        Assert.False(result.SupportedRules[1].SourceEnabled);
    }

    [Fact]
    public void Reports_unsupported_conditions_and_non_local_targets_without_discarding_valid_rules()
    {
        const string file = """
            name="Valid"
            enabled="yes"
            type="17"
            action="Move to folder"
            actionValue="mailbox://nobody@Local%20Folders/Newsletter"
            condition="AND (from,contains,offers)"
            name="Multi-condition"
            enabled="yes"
            type="17"
            action="Move to folder"
            actionValue="mailbox://nobody@Local%20Folders/Newsletter"
            condition="AND (from,contains,offers) AND (subject,contains,weekly)"
            name="Remote target"
            enabled="yes"
            type="17"
            action="Move to folder"
            actionValue="imap://user@example.test/INBOX/Newsletter"
            condition="AND (from,contains,offers)"
            """;

        var result = ThunderbirdRuleFileParser.Parse(file);

        Assert.Single(result.SupportedRules);
        Assert.Equal(2, result.UnsupportedRules.Count);
        Assert.Contains(result.UnsupportedRules, rule => rule.Name == "Multi-condition");
        Assert.Contains(result.UnsupportedRules, rule => rule.Name == "Remote target");
    }

    [Fact]
    public void Flags_very_short_contains_terms_for_review()
    {
        const string file = """
            name="Broad"
            enabled="yes"
            type="17"
            action="Move to folder"
            actionValue="mailbox://nobody@Local%20Folders/Newsletter"
            condition="AND (from,contains,ea)"
            """;

        var result = ThunderbirdRuleFileParser.Parse(file);

        Assert.Contains("Sehr kurzer", Assert.Single(result.SupportedRules).Warning);
    }

    [Fact]
    public void Rejects_empty_files_without_rule_blocks()
    {
        Assert.Throws<FormatException>(() => ThunderbirdRuleFileParser.Parse("version=\"9\""));
    }
}
