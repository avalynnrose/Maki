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
}
