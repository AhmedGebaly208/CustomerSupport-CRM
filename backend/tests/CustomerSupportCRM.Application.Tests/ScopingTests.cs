using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Application.Tests.TestSupport;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Tests;

/// <summary>Department scoping is enforcement, not a convenience filter (PDF area 12).
/// These tests attempt the cross-department access an agent should never get, and assert
/// it fails — a filter that merely tidies a list would pass a "list is scoped" test while
/// still leaking every record to anyone who guesses an id.</summary>
public class ScopingTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Department _support = null!;
    private Department _billing = null!;

    private void SeedTwoDepartments()
    {
        _support = _h.SeedDepartment("SUP");
        _billing = _h.SeedDepartment("BIL");
    }

    private async Task<Customer> SeedCustomerInAsync(Department? department)
    {
        var customer = new Customer
        {
            Code = await _h.Numbers.NextCustomerCodeAsync(),
            FullNameAr = "عميل",
            FullNameEn = "Customer",
            Email = $"{Guid.NewGuid():N}@example.com",
            DepartmentId = department?.Id,
            IsActive = true
        };

        _h.Db.Customers.Add(customer);
        await _h.Db.SaveChangesAsync();
        return customer;
    }

    private async Task<TicketDetailDto> SeedTicketInAsync(Department? department)
    {
        var customer = await SeedCustomerInAsync(department);

        return await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Issue", "Body", TicketPriority.Normal,
            CommunicationChannel.Email, null, department?.Id, null, null));
    }

    // ---- Tickets ----

    [Fact]
    public async Task An_agent_sees_only_their_own_departments_tickets()
    {
        SeedTwoDepartments();
        await SeedTicketInAsync(_support);
        await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        var result = await _h.Tickets.SearchAsync(new TicketQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Support", result.Items[0].DepartmentNameEn);
    }

    [Fact]
    public async Task An_agent_cannot_read_another_departments_ticket_by_id()
    {
        SeedTwoDepartments();
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Tickets.GetByIdAsync(otherTicket.Id));
    }

    [Fact]
    public async Task An_agent_cannot_change_the_status_of_another_departments_ticket()
    {
        SeedTwoDepartments();
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Tickets.ChangeStatusAsync(otherTicket.Id, new ChangeTicketStatusRequest(TicketStatus.Open, null)));
    }

    [Fact]
    public async Task An_agent_cannot_assign_another_departments_ticket()
    {
        SeedTwoDepartments();
        var agent = _h.Identity.AddAgent("Sara");
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Tickets.AssignAsync(otherTicket.Id, new AssignTicketRequest(agent.Id, null)));
    }

    [Fact]
    public async Task An_agent_cannot_edit_or_delete_another_departments_ticket()
    {
        SeedTwoDepartments();
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Tickets.UpdateAsync(otherTicket.Id, new UpdateTicketRequest(
                "Hijacked", "Body", TicketPriority.Urgent, null, null, null)));

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Tickets.DeleteAsync(otherTicket.Id));
    }

    [Fact]
    public async Task An_agent_cannot_read_or_write_comments_on_another_departments_ticket()
    {
        SeedTwoDepartments();
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Tickets.GetCommentsAsync(otherTicket.Id, includeInternal: true));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Tickets.AddCommentAsync(otherTicket.Id, new CreateTicketCommentRequest("Prying", false)));
    }

    [Fact]
    public async Task An_agent_cannot_read_the_history_of_another_departments_ticket()
    {
        SeedTwoDepartments();
        var otherTicket = await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Tickets.GetHistoryAsync(otherTicket.Id));
    }

    // ---- Customers ----

    [Fact]
    public async Task An_agent_sees_only_their_own_departments_customers()
    {
        SeedTwoDepartments();
        await SeedCustomerInAsync(_support);
        await SeedCustomerInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        var result = await _h.Customers.SearchAsync(new CustomerQuery());

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task An_agent_cannot_read_another_departments_customer_by_id()
    {
        SeedTwoDepartments();
        var other = await SeedCustomerInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Customers.GetByIdAsync(other.Id));
    }

    [Fact]
    public async Task An_agent_cannot_reach_another_departments_customer_notes_or_interactions()
    {
        SeedTwoDepartments();
        var other = await SeedCustomerInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Customers.GetNotesAsync(other.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Customers.GetInteractionsAsync(other.Id, 1, 20));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Customers.AddNoteAsync(other.Id, new CreateCustomerNoteRequest("Prying", true)));
    }

    [Fact]
    public async Task An_agent_cannot_edit_or_delete_another_departments_customer()
    {
        SeedTwoDepartments();
        var other = await SeedCustomerInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _h.Customers.UpdateAsync(other.Id, new UpdateCustomerRequest(
                "مخترق", "Hijacked", "x@example.com", null, null, null, null, null,
                "ar", null, null, true, null)));

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Customers.DeleteAsync(other.Id));
    }

    // ---- Unrouted records and fail-closed behaviour ----

    [Fact]
    public async Task Records_with_no_department_stay_visible_so_unrouted_work_is_claimable()
    {
        SeedTwoDepartments();
        await SeedTicketInAsync(null);

        _h.Scope.ScopeTo(_support.Id);

        var result = await _h.Tickets.SearchAsync(new TicketQuery());

        Assert.Equal(1, result.TotalCount);
        Assert.Null(result.Items[0].DepartmentNameEn);
    }

    [Fact]
    public async Task An_agent_with_no_department_sees_nothing_rather_than_everything()
    {
        SeedTwoDepartments();
        await SeedTicketInAsync(_support);
        await SeedTicketInAsync(_billing);
        var unrouted = await SeedTicketInAsync(null);

        // Fail closed: a misconfigured account is a support ticket, not a data leak.
        _h.Scope.ScopeTo(null);

        var result = await _h.Tickets.SearchAsync(new TicketQuery());
        Assert.Equal(0, result.TotalCount);

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Tickets.GetByIdAsync(unrouted.Id));
    }

    [Fact]
    public async Task A_supervisor_keeps_the_global_view()
    {
        SeedTwoDepartments();
        var support = await SeedTicketInAsync(_support);
        var billing = await SeedTicketInAsync(_billing);

        // FakeScopeProvider defaults to global, matching Admin/Manager.
        var result = await _h.Tickets.SearchAsync(new TicketQuery());

        Assert.Equal(2, result.TotalCount);
        Assert.NotNull(await _h.Tickets.GetByIdAsync(support.Id));
        Assert.NotNull(await _h.Tickets.GetByIdAsync(billing.Id));
    }

    [Fact]
    public async Task The_agent_dashboard_unassigned_count_respects_scope()
    {
        SeedTwoDepartments();
        var agent = _h.Identity.AddAgent("Sara", _support.Id);

        // One unassigned ticket in each department.
        await SeedTicketInAsync(_support);
        await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        var board = await _h.Tickets.GetAgentDashboardAsync(agent.Id);

        Assert.Equal(1, board.UnassignedInDepartment);
    }

    [Fact]
    public async Task A_department_filter_cannot_widen_the_scope()
    {
        SeedTwoDepartments();
        await SeedTicketInAsync(_billing);

        _h.Scope.ScopeTo(_support.Id);

        // Explicitly asking for the other department must still return nothing: the scope is
        // applied before the caller's filters, so a filter can only ever narrow further.
        var result = await _h.Tickets.SearchAsync(new TicketQuery { DepartmentId = _billing.Id });

        Assert.Equal(0, result.TotalCount);
    }
}
