using System;
using System.Collections.Generic;
using System.Linq;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public record PlayStyleDefinition(
    string Code,
    string NameVi,
    string NameEn,
    string Category,
    string SvgPath
);

public record AssignedPlayStyle(
    PlayStyleDefinition Definition,
    bool IsPlus
);

public record TeamVibeAnalysis(
    List<string> TopVibes,
    double HardTacklePercentage,
    int TotalVibeReviews,
    bool IsHighHardTackle
);

public static class PlayStyleCatalog
{
    public const string VibeDaoDuongSinh = "Đá dưỡng sinh";
    public const string VibeBanBat = "Ban bật – kỹ thuật";
    public const string VibeTheLuc = "Thể lực – chạy khỏe";
    public const string VibeDaRat = "Đá rát – quyết liệt";
    public const string VibeVuiVe = "Vui vẻ – hay giao lưu tăng 2";

    public static readonly string[] AllowedVibeTags =
    [
        VibeDaoDuongSinh,
        VibeBanBat,
        VibeTheLuc,
        VibeDaRat,
        VibeVuiVe
    ];

    public static readonly List<PlayStyleDefinition> All = new()
    {
        // 1. Dứt điểm (Finishing - 8)
        new("FINESSE", "Chuyên gia cứa lòng", "Finesse Expert", "Dứt điểm",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"),
        new("CLINICAL", "Sát thủ vòng cấm", "Clinical Finisher", "Dứt điểm",
            "M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm1 14h-2v-2h2v2zm0-4h-2V7h2v5z"),
        new("POWER_SHOT", "Sút uy lực", "Power Shot", "Dứt điểm",
            "M7 2v11h3v9l7-12h-4l4-8z"),
        new("PENALTY", "Chuyên gia phạt đền", "Penalty Expert", "Dứt điểm",
            "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-3.31 0-6 2.69-6 6s2.69 6 6 6 6-2.69 6-6-2.69-6-6-6zm0 10c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4z"),
        new("CHIP_SHOT", "Chuyên gia lốp bóng", "Chip Shot", "Dứt điểm",
            "M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 19.4c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l1.9-1.9C9.17 19.58 10.53 20 12 20c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6z"),
        new("CUT_INSIDE", "Sát thủ băng cắt", "Cut Inside", "Dứt điểm",
            "M3.5 18.49l6-6.01 4 4L22 6.92l-1.41-1.41-7.09 7.97-4-4L2 16.99z"),
        new("VOLLEY", "Chuyên gia vô lê", "Volley Expert", "Dứt điểm",
            "M13.5 5.5c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zM9.8 8.9L7 23h2.1l1.8-8 2.1 2v6h2v-7.5l-2.1-2 .6-3C14.8 12 16.8 13 19 13v-2c-1.9 0-3.5-1-4.3-2.4l-1-1.6c-.4-.6-1-1-1.7-1-.3 0-.5.1-.8.1L6 8.3V13h2V9.6l1.8-.7z"),
        new("FIRST_TIME", "Dứt điểm một chạm", "First Time Shot", "Dứt điểm",
            "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 3c1.93 0 3.5 1.57 3.5 3.5S13.93 13 12 13s-3.5-1.57-3.5-3.5S10.07 6 12 6zm-5 13c0-2.5 5-3.9 5-3.9s5 1.4 5 3.9H7z"),

        // 2. Chuyền bóng (Passing - 7)
        new("TIKI_TAKA", "Tiki Taka", "Tiki Taka", "Chuyền bóng",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"),
        new("BULLET_PASS", "Chuyền uy lực", "Bullet Pass", "Chuyền bóng",
            "M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"),
        new("WHIPPED_CROSS", "Tạt xoáy", "Whipped Cross", "Chuyền bóng",
            "M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46C19.54 15.03 20 13.57 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74C4.46 8.97 4 10.43 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z"),
        new("INCISIVE", "Chọc khe sắc bén", "Incisive Through Pass", "Chuyền bóng",
            "M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"),
        new("DRIVEN_LOB", "Chọc khe bổng uy lực", "Driven Lob Through", "Chuyền bóng",
            "M19 15l-6 6-1.42-1.42L15.17 16H4V4h2v10h9.17l-3.59-3.58L13 9l6 6z"),
        new("DEAD_BALL", "Bóng chết", "Dead Ball", "Chuyền bóng",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"),
        new("MAESTRO", "Nhạc trưởng", "Maestro", "Chuyền bóng",
            "M12 3L1 9l4 2.18v6L12 21l7-3.82v-6l2-1.09V17h2V9L12 3zm6.82 6L12 12.72 5.18 9 12 5.28 18.82 9zM17 15.99l-5 2.73-5-2.73v-3.72L12 15l5-2.73v3.72z"),

        // 3. Tốc độ & Kỹ thuật (Pace & Dribbling - 6)
        new("RAPID", "Tốc độ", "Rapid", "Tốc độ & Kỹ thuật",
            "M13.5 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zM9.8 8.9L7 23h2.1l1.8-8 2.1 2v6h2v-7.5l-2.1-2 .6-3C14.8 12 16.8 13 19 13v-2c-1.9 0-3.5-1-4.3-2.4l-1-1.6c-.4-.6-1-1-1.7-1-.3 0-.5.1-.8.1L6 8.3V13h2V9.6l1.8-.7z"),
        new("TRICKSTER", "Kỹ thuật", "Trickster", "Tốc độ & Kỹ thuật",
            "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"),
        new("ACCELERATOR", "Ma tốc độ", "Accelerator", "Tốc độ & Kỹ thuật",
            "M11 21h-1l1-7H7.5c-.88 0-.33-.75-.31-.78C8.48 10.94 10.42 7.54 13.01 3h1l-1 7h3.51c.4 0 .62.19.4.66C14.77 15.01 12.83 18.41 11 21z"),
        new("SUPER_SPRINT", "Siêu bứt tốc", "Super Sprint", "Tốc độ & Kỹ thuật",
            "M15 1H9v2h6V1zm-4 13h2V8h-2v6zm8.03-6.61l1.42-1.42c-.43-.51-.9-.99-1.41-1.41l-1.42 1.42C16.07 4.74 14.12 4 12 4c-4.97 0-9 4.03-9 9s4.02 9 9 9 9-4.03 9-9c0-2.12-.74-4.07-1.97-5.61zM12 20c-3.87 0-7-3.13-7-7s3.13-7 7-7 7 3.13 7 7-3.13 7-7 7z"),
        new("ILLUSIONIST", "Ảo thuật gia", "Illusionist", "Tốc độ & Kỹ thuật",
            "M19 9l1.25-2.75L23 5l-2.75-1.25L19 1l-1.25 2.75L15 5l2.75 1.25L19 9zm-7.5.5L9 4 6.5 9.5 1 12l5.5 2.5L9 20l2.5-5.5L17 12l-5.5-2.5zM19 15l-1.25 2.75L15 19l2.75 1.25L19 23l1.25-2.75L23 19l-2.75-1.25L19 15z"),
        new("OFF_BALL", "Bậc thầy chạy chỗ", "Off-Ball Master", "Tốc độ & Kỹ thuật",
            "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z"),

        // 4. Phòng ngự (Defending - 7)
        new("ANTICIPATE", "Đánh chặn chủ động", "Anticipate", "Phòng ngự",
            "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z"),
        new("GUARDIAN", "Lá chắn thép", "Guardian", "Phòng ngự",
            "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"),
        new("SLIDE_TACKLE", "Xoạc bóng", "Slide Tackle", "Phòng ngự",
            "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z"),
        new("TACKLE_MASTER", "Cao thủ tắc bóng", "Tackle Master", "Phòng ngự",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"),
        new("MAN_MARKER", "Chuyên gia đeo bám", "Man Marker", "Phòng ngự",
            "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"),
        new("STOPPER", "Chặn đà tấn công", "Stopper", "Phòng ngự",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 11H7v-2h10v2z"),
        new("INTERCEPTOR", "Siêu đánh chặn", "Interceptor", "Phòng ngự",
            "M12 2L1 21h22L12 2zm0 3.99L19.53 19H4.47L12 5.99zM11 10h2v4h-2zm0 6h2v2h-2z"),

        // 5. Thể chất & Không chiến (Physical & Aerial - 4)
        new("RELENTLESS", "Không phổi", "Relentless", "Thể chất & Không chiến",
            "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"),
        new("BRUISER", "Cơ bắp thép", "Bruiser", "Thể chất & Không chiến",
            "M20.57 14.86L22 13.43 20.57 12 17 15.57 8.43 7 12 3.43 10.57 2 9.14 3.43 7.71 2 5.57 4.14 4.14 2.71 2.71 4.14l1.43 1.43L2 7.71l1.43 1.43L2 10.57 3.43 12 7 8.43 15.57 17 12 20.57 13.43 22l1.43-1.43L16.29 22l2.14-2.14 1.43 1.43 1.43-1.43-1.43-1.43L22 16.29z"),
        new("PRECISION_HEAD", "Chuyên gia không chiến", "Precision Header", "Thể chất & Không chiến",
            "M12 2c1.1 0 2 .9 2 2s-.9 2-2 2-2-.9-2-2 .9-2 2-2zm9 7h-6v13h-2v-6h-2v6H9V9H3V7h18v2z"),
        new("AERIAL_MASTER", "Bậc thầy không chiến", "Aerial Master", "Thể chất & Không chiến",
            "M21 3L3 10.53v.98l6.84 2.65L12.48 21h.98L21 3z"),

        // 6. Thủ môn (Goalkeeping - 5)
        new("DEFLECTOR", "Cản phá bóng", "Deflector", "Thủ môn",
            "M18 10h-1.26A8 8 0 109 20h9a5 5 0 000-10zm-1 8h-8a6 6 0 114.71-9.72c.45.24.96.38 1.49.44A3 3 0 0118 11.83 3 3 0 0117 18z"),
        new("RUSH_OUT", "Thủ môn băng ra", "Rush Out", "Thủ môn",
            "M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z"),
        new("PROACTIVE_GK", "Chủ động cản phá", "Proactive GK", "Thủ môn",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"),
        new("GK_AERIAL", "Áp đảo không chiến (GK)", "Aerial Dominance GK", "Thủ môn",
            "M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm6 9.09c0 4-2.55 7.7-6 8.83-3.45-1.13-6-4.82-6-8.83V6.31l6-2.25 6 2.25v4.78z"),
        new("GK_LONG_SHOT", "GK bắt bài sút xa", "Long-Shot Anticipation GK", "Thủ môn",
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14H9V8h2v8zm4 0h-2V8h2v8z")
    };

    private static readonly Dictionary<string, PlayStyleDefinition> ByCode =
        All.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public static PlayStyleDefinition? Get(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var clean = code.Trim().TrimEnd('+');
        return ByCode.TryGetValue(clean, out var def) ? def : null;
    }

    public static List<AssignedPlayStyle> Parse(string? raw)
    {
        var list = new List<AssignedPlayStyle>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in tokens)
        {
            bool isPlus = t.EndsWith('+');
            var code = t.TrimEnd('+');
            if (ByCode.TryGetValue(code, out var def))
            {
                list.Add(new AssignedPlayStyle(def, isPlus));
            }
        }
        return list;
    }

    public static (bool IsValid, string? Error, string Formatted) ValidateAndFormat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (true, null, string.Empty);

        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count > 4)
            return (false, "Mỗi cầu thủ chỉ được gán tối đa 4 PlayStyles (tối đa 1 PlayStyle+ và 3 thường).", string.Empty);

        int plusCount = tokens.Count(t => t.EndsWith('+'));
        if (plusCount > 1)
            return (false, "Chỉ được chọn tối đa 1 PlayStyle+ (vàng).", string.Empty);

        int regCount = tokens.Count - plusCount;
        if (regCount > 3)
            return (false, "Chỉ được chọn tối đa 3 PlayStyle thường.", string.Empty);

        var validTokens = new List<string>();
        foreach (var t in tokens)
        {
            bool isPlus = t.EndsWith('+');
            var code = t.TrimEnd('+').ToUpperInvariant();
            if (!ByCode.ContainsKey(code))
                return (false, $"Mã kỹ năng '{code}' không tồn tại trong hệ thống.", string.Empty);

            validTokens.Add(isPlus ? $"{code}+" : code);
        }

        // Đảm bảo PlayStyle+ đứng đầu nếu có
        validTokens = validTokens.OrderByDescending(t => t.EndsWith('+')).ToList();
        return (true, null, string.Join(",", validTokens));
    }

    public static TeamVibeAnalysis AnalyzeTeamVibe(IEnumerable<TeamMatchReview> reviews)
    {
        var validReviews = reviews?.Where(r => r.IsValid && !string.IsNullOrWhiteSpace(r.PlayStyleVibeTags)).ToList() ?? new();
        if (validReviews.Count == 0)
        {
            return new TeamVibeAnalysis(new List<string>(), 0.0, 0, false);
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalWithVibes = validReviews.Count;
        int hardTackleCount = 0;

        foreach (var rev in validReviews)
        {
            var tags = (rev.PlayStyleVibeTags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool reviewHasHardTackle = false;
            foreach (var tag in tags)
            {
                if (AllowedVibeTags.Contains(tag))
                {
                    counts[tag] = counts.GetValueOrDefault(tag, 0) + 1;
                    if (string.Equals(tag, VibeDaRat, StringComparison.OrdinalIgnoreCase))
                    {
                        reviewHasHardTackle = true;
                    }
                }
            }
            if (reviewHasHardTackle) hardTackleCount++;
        }

        double hardTacklePct = totalWithVibes > 0 ? (double)hardTackleCount / totalWithVibes * 100.0 : 0.0;
        var topVibes = counts.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToList();

        return new TeamVibeAnalysis(topVibes, Math.Round(hardTacklePct, 1), totalWithVibes, hardTacklePct >= 35.0);
    }
}
