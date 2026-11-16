using System;
using System.Collections.Generic;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Xunit;

namespace AloBall.Tests;

public class PlayStyleCatalogTests
{
    [Fact]
    public void Catalog_Contains37UniqueSkillsAcross6Categories()
    {
        Assert.Equal(37, PlayStyleCatalog.All.Count);

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in PlayStyleCatalog.All)
        {
            Assert.True(codes.Add(s.Code), $"Duplicate code: {s.Code}");
            Assert.False(string.IsNullOrWhiteSpace(s.NameVi));
            Assert.False(string.IsNullOrWhiteSpace(s.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(s.SvgPath));
        }
    }

    [Theory]
    [InlineData("FINESSE+,INCISIVE,RAPID,RELENTLESS", true, null)]
    [InlineData("FINESSE+,POWER_SHOT+", false, "Chỉ được chọn tối đa 1 PlayStyle+ (vàng).")]
    [InlineData("FINESSE,CLINICAL,POWER_SHOT,PENALTY", false, "Chỉ được chọn tối đa 3 PlayStyle thường.")]
    [InlineData("INVALID_CODE", false, "không tồn tại trong hệ thống")]
    public void ValidateAndFormat_EnforcesPlayStyleRules(string input, bool expectedValid, string? expectedErrorSubstring)
    {
        var (isValid, error, formatted) = PlayStyleCatalog.ValidateAndFormat(input);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid && expectedErrorSubstring != null)
        {
            Assert.Contains(expectedErrorSubstring, error);
        }
        else if (expectedValid)
        {
            Assert.NotEmpty(formatted);
        }
    }

    [Fact]
    public void AnalyzeTeamVibe_ComputesHardTacklePercentageCorrectly()
    {
        var reviews = new List<TeamMatchReview>
        {
            new() { IsValid = true, PlayStyleVibeTags = "Đá rát – quyết liệt,Thể lực – chạy khỏe" },
            new() { IsValid = true, PlayStyleVibeTags = "Đá rát – quyết liệt,Ban bật – kỹ thuật" },
            new() { IsValid = true, PlayStyleVibeTags = "Đá dưỡng sinh,Vui vẻ – hay giao lưu tăng 2" }
        };

        var analysis = PlayStyleCatalog.AnalyzeTeamVibe(reviews);
        Assert.Equal(3, analysis.TotalVibeReviews);
        Assert.True(analysis.HardTacklePercentage > 35.0); // 2 out of 3 = 66.7%
        Assert.True(analysis.IsHighHardTackle);
        Assert.Contains("Đá rát – quyết liệt", analysis.TopVibes);
    }
}
