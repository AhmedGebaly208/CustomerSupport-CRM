using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Reports.Dtos;

public enum ReportGranularity
{
    Day = 0,
    Week = 1,
    Month = 2
}

public enum ReportKind
{
    Tickets = 0,
    Sla = 1,
    Agents = 2,
    Csat = 3
}

public enum ExportFormat
{
    Xlsx = 0,
    Csv = 1
}

/// <summary>What a report is asked for. Filters are all optional and narrow the same base
/// set, so one query shape serves every report.</summary>
public sealed record ReportQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    ReportGranularity Granularity = ReportGranularity.Day,
    Guid? DepartmentId = null,
    Guid? BranchId = null,
    Guid? CategoryId = null,
    TicketPriority? Priority = null,
    TicketStatus? Status = null,
    CommunicationChannel? Channel = null,
    Guid? AgentId = null);

public sealed record TimeBucketDto(DateTimeOffset Start, int Count);

public sealed record DimensionBucketDto(string Key, string LabelAr, string LabelEn, int Count);

/// <summary>Counts over time with the same window immediately before it, so a number is
/// read against where it was rather than in isolation.</summary>
public sealed record TicketReportDto(
    int Total,
    int PreviousTotal,
    IReadOnlyList<TimeBucketDto> Trend,
    IReadOnlyList<TimeBucketDto> PreviousTrend,
    IReadOnlyList<DimensionBucketDto> ByStatus,
    IReadOnlyList<DimensionBucketDto> ByPriority,
    IReadOnlyList<DimensionBucketDto> ByChannel,
    IReadOnlyList<DimensionBucketDto> ByCategory,
    IReadOnlyList<DimensionBucketDto> ByDepartment);

/// <summary>Attainment and timings. Every ratio is nullable: with no tickets in the window
/// the answer is "nothing to report", and rendering that as 0% would read as total failure.</summary>
public sealed record SlaReportDto(
    int Measured,
    int FirstResponseMet,
    int FirstResponseBreached,
    double? FirstResponseAttainment,
    int ResolutionMet,
    int ResolutionBreached,
    double? ResolutionAttainment,
    double? AverageFirstResponseMinutes,
    double? MedianFirstResponseMinutes,
    double? AverageResolutionMinutes,
    double? MedianResolutionMinutes,
    IReadOnlyList<TimeBucketDto> BreachTrend,
    IReadOnlyList<DimensionBucketDto> BreachesByDepartment);

public sealed record AgentRowDto(
    Guid AgentId,
    string NameAr,
    string NameEn,
    int Handled,
    int Resolved,
    int Reopened,
    double? ReopenRate,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes,
    double? AverageSatisfaction,
    int CurrentLoad);

public sealed record AgentReportDto(IReadOnlyList<AgentRowDto> Rows);

public sealed record CsatReportDto(
    double? AverageScore,
    int Responses,
    int EligibleTickets,
    double? ResponseRate,
    IReadOnlyList<TimeBucketDto> Trend,
    /// <summary>How many gave each score, 1 to 5. A mean hides a split between delighted and
    /// furious, which is exactly the case worth seeing.</summary>
    IReadOnlyList<DimensionBucketDto> Distribution);

public sealed record ReportExportRequest(ReportKind Kind, ExportFormat Format, ReportQuery Query);

public sealed record SubmitSatisfactionRequest(int Score, string? Comment);

public sealed record SatisfactionDto(Guid TicketId, int Score, string? Comment, DateTimeOffset SubmittedAt);
