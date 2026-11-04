using Maki.Core.Quality;

namespace Maki.Core.Tests;

public class LanguageFallbackRuleTests
{
    private static LanguageFallbackRule.MappingSamples Mapping(int id, params int[] pages) => new(id, pages);

    [Fact]
    public void A_series_with_no_primary_source_falls_back_for_that_reason() =>
        Assert.Equal(LanguageFallbackReason.NoSource, LanguageFallbackRule.Evaluate([]));

    [Fact]
    public void Every_source_measured_as_previews_falls_back_for_that_reason() =>
        Assert.Equal(LanguageFallbackReason.PreviewsOnly,
            LanguageFallbackRule.Evaluate([Mapping(1, 7, 9, 12), Mapping(2, 1, 1, 2)]));

    [Fact]
    public void One_usable_source_among_previews_keeps_the_primary_language() =>
        Assert.Null(LanguageFallbackRule.Evaluate([Mapping(1, 7, 9, 12), Mapping(2, 28, 31, 30), Mapping(3, 2, 2, 2)]));

    [Fact]
    public void A_source_measured_once_is_not_judged() =>
        Assert.Null(LanguageFallbackRule.Evaluate([Mapping(1, 7, 9), Mapping(2, 3)]));

    [Fact]
    public void A_source_that_could_not_be_measured_keeps_the_primary_language() =>
        Assert.Null(LanguageFallbackRule.Evaluate([Mapping(1, 7, 9), Mapping(2)]));

    [Fact]
    public void Two_samples_are_enough_to_judge_a_source() =>
        Assert.True(LanguageFallbackRule.ServesPreviewsOnly([3, 5]));

    [Theory]
    [InlineData(new[] { 14, 14, 14 }, true)]
    [InlineData(new[] { 15, 15, 15 }, false)]
    [InlineData(new[] { 8, 20, 22 }, false)]
    [InlineData(new[] { 8, 9, 40 }, true)]
    [InlineData(new[] { 10, 20 }, false)]
    [InlineData(new[] { 10, 18 }, true)]
    [InlineData(new[] { 1, 2, 3 }, true)]
    public void The_median_page_count_is_compared_with_the_preview_threshold(int[] pages, bool previews) =>
        Assert.Equal(previews, LanguageFallbackRule.ServesPreviewsOnly(pages));

    [Fact]
    public void A_source_that_publishes_no_chapters_is_not_judged()
    {
        // One source lists chapters and serves previews; eleven list nothing and have no samples.
        var silent = Enumerable.Range(2, 11).Select(id => new LanguageFallbackRule.MappingSamples(id, [], false));
        Assert.Equal(
            LanguageFallbackReason.PreviewsOnly,
            LanguageFallbackRule.Evaluate([Mapping(1, 11, 7, 7), .. silent]));
    }

    [Fact]
    public void A_publishing_source_that_was_not_measured_still_keeps_the_primary_language()
    {
        var silent = new LanguageFallbackRule.MappingSamples(3, [], false);
        Assert.Null(LanguageFallbackRule.Evaluate([Mapping(1, 11, 7, 7), Mapping(2), silent]));
    }

    [Fact]
    public void Sources_that_all_publish_nothing_are_no_evidence_of_previews()
    {
        Assert.Null(LanguageFallbackRule.Evaluate([new(1, [], false), new(2, [], false)]));
    }
}
