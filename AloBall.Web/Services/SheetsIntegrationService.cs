using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public class SheetsImportPreviewItem
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool HasConflictOrError { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ISheetsIntegrationService
{
    Task<byte[]> ExportAttendanceReportAsync(Guid teamId, string period);
    Task<byte[]> ExportPlayersAndKitsAsync(Guid teamId);
    Task<List<SheetsImportPreviewItem>> PreviewPlayersImportAsync(Guid teamId, Stream excelStream);
    Task<(int UpdatedCount, List<string> Errors)> ApplyPlayersImportAsync(Guid teamId, List<SheetsImportPreviewItem> approvedChanges);
}

public class SheetsIntegrationService : ISheetsIntegrationService
{
    private readonly ApplicationDbContext _db;

    public SheetsIntegrationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<byte[]> ExportAttendanceReportAsync(Guid teamId, string period)
    {
        var team = await _db.Teams.FindAsync(teamId);
        var sessions = await _db.AttendanceSessions
            .Include(s => s.Records)
            .ThenInclude(r => r.Player)
            .Where(s => s.TeamId == teamId && s.StatisticalPeriod == period && s.Status != SessionStatus.Cancelled)
            .OrderBy(s => s.SessionDateTime)
            .ToListAsync();

        var players = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .OrderBy(p => p.KitNumber ?? 999)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"Chuyên cần {period}");

        // Tiêu đề
        worksheet.Cell(1, 1).Value = $"BẢNG CHUYÊN CẦN & ĐIỂM DANH - {team?.Name ?? "ĐỘI BÓNG"}";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;

        worksheet.Cell(2, 1).Value = $"Kỳ thống kê: {period} | Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm}";
        worksheet.Cell(2, 1).Style.Font.Italic = true;

        // Header bảng
        worksheet.Cell(4, 1).Value = "ID Cầu thủ";
        worksheet.Cell(4, 2).Value = "Số áo";
        worksheet.Cell(4, 3).Value = "Họ và tên";
        worksheet.Cell(4, 4).Value = "Vị trí";

        int colIndex = 5;
        foreach (var session in sessions)
        {
            worksheet.Cell(4, colIndex).Value = $"{session.SessionDateTime:dd/MM} ({session.Title})";
            colIndex++;
        }

        worksheet.Cell(4, colIndex).Value = "Số trận tham gia";
        worksheet.Cell(4, colIndex + 1).Value = "Tổng tiền phạt (VND)";

        var headerRange = worksheet.Range(4, 1, 4, colIndex + 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(220, 230, 242);

        // Data rows
        int rowIndex = 5;
        foreach (var player in players)
        {
            worksheet.Cell(rowIndex, 1).Value = player.Id.ToString();
            worksheet.Cell(rowIndex, 2).Value = player.KitNumber?.ToString() ?? "";
            worksheet.Cell(rowIndex, 3).Value = player.FullName;
            worksheet.Cell(rowIndex, 4).Value = player.Position ?? "";

            int attendedCount = 0;
            decimal totalFines = 0;

            int sCol = 5;
            foreach (var session in sessions)
            {
                var record = session.Records.FirstOrDefault(r => r.PlayerId == player.Id);
                if (record != null)
                {
                    string statusText = record.Actual switch
                    {
                        ActualStatus.Present => "Có mặt",
                        ActualStatus.Absent => "Vắng",
                        _ => "Chưa ĐD"
                    };
                    worksheet.Cell(rowIndex, sCol).Value = statusText;

                    if (record.Actual == ActualStatus.Present) attendedCount++;
                    totalFines += record.FineAmount;
                }
                else
                {
                    worksheet.Cell(rowIndex, sCol).Value = "-";
                }
                sCol++;
            }

            worksheet.Cell(rowIndex, colIndex).Value = attendedCount;
            worksheet.Cell(rowIndex, colIndex + 1).Value = totalFines;
            rowIndex++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportPlayersAndKitsAsync(Guid teamId)
    {
        var team = await _db.Teams.FindAsync(teamId);
        var players = await _db.Players
            .Where(p => p.TeamId == teamId)
            .OrderBy(p => p.KitNumber ?? 999)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Danh sách cầu thủ & Size");

        ws.Cell(1, 1).Value = $"DANH SÁCH CẦU THỦ & ĐẶT ÁO - {team?.Name}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(3, 1).Value = "PlayerId (Không sửa cột này)";
        ws.Cell(3, 2).Value = "Họ và tên";
        ws.Cell(3, 3).Value = "Số áo";
        ws.Cell(3, 4).Value = "Vị trí";
        ws.Cell(3, 5).Value = "Tên in áo";
        ws.Cell(3, 6).Value = "Size áo";
        ws.Cell(3, 7).Value = "Size quần";
        ws.Cell(3, 8).Value = "Chiều cao (cm)";
        ws.Cell(3, 9).Value = "Cân nặng (kg)";

        var header = ws.Range(3, 1, 3, 9);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromArgb(226, 239, 218);

        int row = 4;
        foreach (var p in players)
        {
            ws.Cell(row, 1).Value = p.Id.ToString();
            ws.Cell(row, 2).Value = p.FullName;
            ws.Cell(row, 3).Value = p.KitNumber?.ToString() ?? "";
            ws.Cell(row, 4).Value = p.Position ?? "";
            ws.Cell(row, 5).Value = p.ShirtName ?? "";
            ws.Cell(row, 6).Value = p.ShirtSize ?? "";
            ws.Cell(row, 7).Value = p.ShortSize ?? "";
            ws.Cell(row, 8).Value = p.HeightCm?.ToString() ?? "";
            ws.Cell(row, 9).Value = p.WeightKg?.ToString() ?? "";
            row++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<List<SheetsImportPreviewItem>> PreviewPlayersImportAsync(Guid teamId, Stream excelStream)
    {
        var previewList = new List<SheetsImportPreviewItem>();
        using var workbook = new XLWorkbook(excelStream);
        var ws = workbook.Worksheets.FirstOrDefault();
        if (ws == null) return previewList;

        var players = await _db.Players
            .Where(p => p.TeamId == teamId)
            .ToDictionaryAsync(p => p.Id);

        int row = 4;
        while (!ws.Cell(row, 1).IsEmpty())
        {
            string idStr = ws.Cell(row, 1).GetString().Trim();
            if (Guid.TryParse(idStr, out var playerId))
            {
                if (players.TryGetValue(playerId, out var player))
                {
                    // Kiểm tra thay đổi từng trường được phép (ShirtName, ShirtSize, ShortSize, KitNumber, Position)
                    string newShirtName = ws.Cell(row, 5).GetString().Trim();
                    if ((player.ShirtName ?? "") != newShirtName)
                    {
                        previewList.Add(new SheetsImportPreviewItem
                        {
                            PlayerId = player.Id,
                            PlayerName = player.FullName,
                            FieldName = "Tên in áo",
                            CurrentValue = player.ShirtName ?? "(trống)",
                            NewValue = newShirtName,
                            HasConflictOrError = false
                        });
                    }

                    string newShirtSize = ws.Cell(row, 6).GetString().Trim();
                    if ((player.ShirtSize ?? "") != newShirtSize)
                    {
                        previewList.Add(new SheetsImportPreviewItem
                        {
                            PlayerId = player.Id,
                            PlayerName = player.FullName,
                            FieldName = "Size áo",
                            CurrentValue = player.ShirtSize ?? "(trống)",
                            NewValue = newShirtSize,
                            HasConflictOrError = false
                        });
                    }

                    string newShortSize = ws.Cell(row, 7).GetString().Trim();
                    if ((player.ShortSize ?? "") != newShortSize)
                    {
                        previewList.Add(new SheetsImportPreviewItem
                        {
                            PlayerId = player.Id,
                            PlayerName = player.FullName,
                            FieldName = "Size quần",
                            CurrentValue = player.ShortSize ?? "(trống)",
                            NewValue = newShortSize,
                            HasConflictOrError = false
                        });
                    }
                }
                else
                {
                    previewList.Add(new SheetsImportPreviewItem
                    {
                        PlayerId = playerId,
                        PlayerName = ws.Cell(row, 2).GetString(),
                        FieldName = "Player",
                        CurrentValue = "N/A",
                        NewValue = "N/A",
                        HasConflictOrError = true,
                        ErrorMessage = "ID cầu thủ không thuộc đội bóng này."
                    });
                }
            }
            row++;
        }

        return previewList;
    }

    public async Task<(int UpdatedCount, List<string> Errors)> ApplyPlayersImportAsync(Guid teamId, List<SheetsImportPreviewItem> approvedChanges)
    {
        var errors = new List<string>();
        int updated = 0;

        foreach (var change in approvedChanges.Where(c => !c.HasConflictOrError))
        {
            var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == change.PlayerId && p.TeamId == teamId);
            if (player == null)
            {
                errors.Add($"Không tìm thấy cầu thủ ID {change.PlayerId} thuộc đội hiện tại.");
                continue;
            }

            if (change.FieldName == "Tên in áo") player.ShirtName = change.NewValue;
            else if (change.FieldName == "Size áo") player.ShirtSize = change.NewValue;
            else if (change.FieldName == "Size quần") player.ShortSize = change.NewValue;

            updated++;
        }

        await _db.SaveChangesAsync();
        return (updated, errors);
    }
}
