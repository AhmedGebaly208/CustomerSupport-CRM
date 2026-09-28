using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Sla;

public interface ISlaPolicyService
{
    Task<IReadOnlyList<SlaPolicyDto>> ListAsync(CancellationToken ct = default);
    Task<SlaPolicyDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<SlaPolicyDto> CreateAsync(SaveSlaPolicyRequest request, CancellationToken ct = default);
    Task<SlaPolicyDto> UpdateAsync(Guid id, SaveSlaPolicyRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<SlaEscalationRuleDto> AddRuleAsync(
        Guid policyId, SaveSlaEscalationRuleRequest request, CancellationToken ct = default);
    Task<SlaEscalationRuleDto> UpdateRuleAsync(
        Guid policyId, Guid ruleId, SaveSlaEscalationRuleRequest request, CancellationToken ct = default);
    Task DeleteRuleAsync(Guid policyId, Guid ruleId, CancellationToken ct = default);
}

/// <summary>Administration of SLA policies and their escalation rules (PDF area 5).
///
/// Editing a policy does not restamp the tickets already carrying it. That is deliberate: a
/// due date is a promise that was made at a point in time, and silently moving thousands of
/// them because someone corrected a typo would rewrite history. Re-applying is an explicit
/// action on the ticket.</summary>
public sealed class SlaPolicyService(
    IAppDbContext db,
    IIdentityService identity,
    IClock clock,
    ICurrentUser currentUser) : ISlaPolicyService
{
    public async Task<IReadOnlyList<SlaPolicyDto>> ListAsync(CancellationToken ct = default)
    {
        var policies = await LoadQuery().ToListAsync(ct);
        var names = await ResolveReassignNamesAsync(policies, ct);

        return policies
            .OrderByDescending(p => p.Specificity)
            .ThenByDescending(p => p.Rank)
            .ThenBy(p => p.NameEn)
            .Select(p => ToDto(p, names))
            .ToList();
    }

    public async Task<SlaPolicyDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await LoadQuery().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(SlaPolicy), id);

        return ToDto(policy, await ResolveReassignNamesAsync([policy], ct));
    }

    public async Task<SlaPolicyDto> CreateAsync(SaveSlaPolicyRequest request, CancellationToken ct = default)
    {
        await GuardAsync(request, ct);

        var policy = new SlaPolicy();
        Apply(policy, request);

        // The parent is new, so its targets ride along with its insert.
        foreach (var target in BuildTargets(policy.Id, request.Targets))
            policy.Targets.Add(target);

        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);

        return await GetAsync(policy.Id, ct);
    }

    public async Task<SlaPolicyDto> UpdateAsync(Guid id, SaveSlaPolicyRequest request, CancellationToken ct = default)
    {
        await GuardAsync(request, ct);

        var policy = await db.SlaPolicies
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(SlaPolicy), id);

        Apply(policy, request);

        // Targets are replaced wholesale. They are added to the set rather than through the
        // navigation because ids are generated in the domain, and EF reads a set key on an
        // entity reached through a tracked parent as an existing row — which turns the
        // insert into an update of a row that was never written.
        db.SlaTargets.RemoveRange(policy.Targets);
        policy.Targets.Clear();
        db.SlaTargets.AddRange(BuildTargets(policy.Id, request.Targets));

        await db.SaveChangesAsync(ct);

        return await GetAsync(policy.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(SlaPolicy), id);

        var inUse = await db.Tickets.CountAsync(t => t.SlaPolicyId == id, ct);

        if (inUse > 0)
        {
            throw new ConflictException(
                $"{inUse} ticket(s) reference this policy. Deactivate it instead of deleting it.",
                ErrorCodes.SlaPolicyInUse);
        }

        policy.IsDeleted = true;
        policy.DeletedAt = clock.UtcNow;
        policy.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task<SlaEscalationRuleDto> AddRuleAsync(
        Guid policyId, SaveSlaEscalationRuleRequest request, CancellationToken ct = default)
    {
        await EnsurePolicyExistsAsync(policyId, ct);
        GuardRule(request);

        var rule = new SlaEscalationRule { SlaPolicyId = policyId };
        ApplyRule(rule, request);

        db.SlaEscalationRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return ToRuleDto(rule, await ResolveReassignNameAsync(rule.ReassignToUserId, ct));
    }

    public async Task<SlaEscalationRuleDto> UpdateRuleAsync(
        Guid policyId, Guid ruleId, SaveSlaEscalationRuleRequest request, CancellationToken ct = default)
    {
        GuardRule(request);

        var rule = await db.SlaEscalationRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.SlaPolicyId == policyId, ct)
            ?? throw new NotFoundException(nameof(SlaEscalationRule), ruleId);

        ApplyRule(rule, request);
        await db.SaveChangesAsync(ct);

        return ToRuleDto(rule, await ResolveReassignNameAsync(rule.ReassignToUserId, ct));
    }

    public async Task DeleteRuleAsync(Guid policyId, Guid ruleId, CancellationToken ct = default)
    {
        var rule = await db.SlaEscalationRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.SlaPolicyId == policyId, ct)
            ?? throw new NotFoundException(nameof(SlaEscalationRule), ruleId);

        rule.IsDeleted = true;
        rule.DeletedAt = clock.UtcNow;
        rule.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private IQueryable<SlaPolicy> LoadQuery() =>
        db.SlaPolicies.AsNoTracking()
            .Include(p => p.Targets)
            .Include(p => p.EscalationRules)
            .Include(p => p.Department)
            .Include(p => p.Branch)
            .Include(p => p.Category);

    private static void Apply(SlaPolicy policy, SaveSlaPolicyRequest request)
    {
        policy.NameAr = request.NameAr.Trim();
        policy.NameEn = request.NameEn.Trim();
        policy.IsActive = request.IsActive;
        policy.Rank = request.Rank;
        policy.DepartmentId = request.DepartmentId;
        policy.BranchId = request.BranchId;
        policy.CategoryId = request.CategoryId;
        policy.CountsBusinessHoursOnly = request.CountsBusinessHoursOnly;
        policy.AssignmentStrategy = request.AssignmentStrategy;

        policy.PausedStatuses = request.PausedStatuses is { Count: > 0 }
            ? string.Join(',', request.PausedStatuses.Distinct())
            : string.Empty;
    }

    private static List<SlaTarget> BuildTargets(Guid policyId, IReadOnlyList<SaveSlaTargetRequest> targets) =>
        targets.Select(t => new SlaTarget
        {
            SlaPolicyId = policyId,
            Priority = t.Priority,
            FirstResponseMinutes = t.FirstResponseMinutes,
            ResolutionMinutes = t.ResolutionMinutes
        }).ToList();

    private static void ApplyRule(SlaEscalationRule rule, SaveSlaEscalationRuleRequest request)
    {
        rule.NameAr = request.NameAr.Trim();
        rule.NameEn = request.NameEn.Trim();
        rule.IsActive = request.IsActive;
        rule.Target = request.Target;
        rule.ThresholdPercent = request.ThresholdPercent;
        rule.RaiseLevelBy = request.RaiseLevelBy;
        rule.ReassignToUserId = request.ReassignToUserId;
        rule.NotifyRole = string.IsNullOrWhiteSpace(request.NotifyRole) ? null : request.NotifyRole.Trim();
    }

    private async Task GuardAsync(SaveSlaPolicyRequest request, CancellationToken ct)
    {
        if (request.Targets.Count == 0)
            throw new BadRequestException("A policy needs at least one priority target.");

        var duplicated = request.Targets.GroupBy(t => t.Priority).Any(g => g.Count() > 1);
        if (duplicated)
            throw new BadRequestException("Each priority may appear only once in a policy.");

        foreach (var target in request.Targets)
        {
            if (target.FirstResponseMinutes <= 0 || target.ResolutionMinutes <= 0)
                throw new BadRequestException("Targets must be greater than zero minutes.");

            // A resolution target shorter than the first-response target is unreachable: the
            // ticket would breach resolution before anyone was due to reply.
            if (target.ResolutionMinutes < target.FirstResponseMinutes)
            {
                throw new BadRequestException(
                    $"The resolution target for '{target.Priority}' is shorter than its first-response target.");
            }
        }

        if (request.DepartmentId is { } departmentId
            && !await db.Departments.AnyAsync(d => d.Id == departmentId, ct))
        {
            throw new NotFoundException(nameof(Department), departmentId);
        }

        if (request.BranchId is { } branchId
            && !await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            throw new NotFoundException(nameof(Branch), branchId);
        }

        if (request.CategoryId is { } categoryId
            && !await db.TicketCategories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new NotFoundException(nameof(TicketCategory), categoryId);
        }
    }

    private static void GuardRule(SaveSlaEscalationRuleRequest request)
    {
        if (request.ThresholdPercent is < 1 or > 500)
            throw new BadRequestException("The threshold must be between 1 and 500 percent.");

        if (request.RaiseLevelBy is < 0 or > 10)
            throw new BadRequestException("The escalation step must be between 0 and 10.");
    }

    private async Task EnsurePolicyExistsAsync(Guid policyId, CancellationToken ct)
    {
        if (!await db.SlaPolicies.AnyAsync(p => p.Id == policyId, ct))
            throw new NotFoundException(nameof(SlaPolicy), policyId);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveReassignNamesAsync(
        IReadOnlyList<SlaPolicy> policies, CancellationToken ct)
    {
        var ids = policies
            .SelectMany(p => p.EscalationRules)
            .Where(r => r.ReassignToUserId is not null)
            .Select(r => r.ReassignToUserId!.Value)
            .Distinct()
            .ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await identity.GetUserDisplayNamesAsync(ids, ct);
    }

    private async Task<string?> ResolveReassignNameAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is null) return null;

        var names = await identity.GetUserDisplayNamesAsync([userId.Value], ct);
        return names.TryGetValue(userId.Value, out var name) ? name : null;
    }

    private static SlaPolicyDto ToDto(SlaPolicy p, IReadOnlyDictionary<Guid, string> names) => new(
        p.Id, p.NameAr, p.NameEn, p.IsActive, p.Rank,
        p.DepartmentId, p.Department?.NameAr, p.Department?.NameEn,
        p.BranchId, p.Branch?.NameAr, p.Branch?.NameEn,
        p.CategoryId, p.Category?.NameAr, p.Category?.NameEn,
        p.CountsBusinessHoursOnly,
        SlaService.ParsePausedStatuses(p.PausedStatuses).OrderBy(s => s).ToList(),
        p.AssignmentStrategy,
        p.Targets.OrderBy(t => t.Priority)
            .Select(t => new SlaTargetDto(t.Priority, t.FirstResponseMinutes, t.ResolutionMinutes))
            .ToList(),
        p.EscalationRules.OrderBy(r => r.ThresholdPercent)
            .Select(r => ToRuleDto(r, r.ReassignToUserId is { } id && names.TryGetValue(id, out var n) ? n : null))
            .ToList());

    private static SlaEscalationRuleDto ToRuleDto(SlaEscalationRule r, string? reassignToName) => new(
        r.Id, r.NameAr, r.NameEn, r.IsActive, r.Target, r.ThresholdPercent,
        r.RaiseLevelBy, r.ReassignToUserId, reassignToName, r.NotifyRole);
}
