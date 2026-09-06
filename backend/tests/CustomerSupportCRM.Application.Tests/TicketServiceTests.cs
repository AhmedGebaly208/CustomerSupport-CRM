using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Tests.TestSupport;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tests;

public class TicketServiceTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<TicketDetailDto> CreateTicketAsync(TicketPriority priority = TicketPriority.Normal)
    {
        var customer = await _h.SeedCustomerAsync();

        return await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Cannot sign in", "The portal rejects my password.",
            priority, CommunicationChannel.Email, null, null, null, null));
    }

    [Fact]
    public async Task Create_assigns_a_sequential_number_and_starts_as_New()
    {
        var first = await CreateTicketAsync();
        var second = await CreateTicketAsync();

        Assert.Equal("TKT-000001", first.Number);
        Assert.Equal("TKT-000002", second.Number);
        Assert.Equal(TicketStatus.New, first.Status);
    }

    [Fact]
    public async Task Create_inherits_the_customers_department_when_none_is_given()
    {
        var department = _h.SeedDepartment();
        var customer = await _h.SeedCustomerAsync();
        customer.DepartmentId = department.Id;
        await _h.Db.SaveChangesAsync();

        var ticket = await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Billing question", "My invoice looks wrong.",
            TicketPriority.Normal, CommunicationChannel.Phone, null, null, null, null));

        Assert.Equal(department.Id, ticket.DepartmentId);
    }

    [Fact]
    public async Task Create_records_the_ticket_as_an_inbound_customer_interaction()
    {
        var ticket = await CreateTicketAsync();

        var interaction = await _h.Db.Interactions.SingleAsync(i => i.TicketId == ticket.Id);

        Assert.Equal(InteractionDirection.Inbound, interaction.Direction);
        Assert.Equal(CommunicationChannel.Email, interaction.Channel);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_customer()
    {
        var request = new CreateTicketRequest(
            Guid.NewGuid(), "Orphan", "No such customer.",
            TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null);

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Tickets.CreateAsync(request));
    }

    [Fact]
    public async Task ChangeStatus_follows_the_workflow_and_records_history()
    {
        var ticket = await CreateTicketAsync();

        var updated = await _h.Tickets.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Open, "Picked up."));

        Assert.Equal(TicketStatus.Open, updated.Status);

        var history = await _h.Tickets.GetHistoryAsync(ticket.Id);
        var statusChange = history.Single(h => h.Field == "Status");

        Assert.Equal("New", statusChange.OldValue);
        Assert.Equal("Open", statusChange.NewValue);
        Assert.Equal("Picked up.", statusChange.Note);
    }

    [Fact]
    public async Task ChangeStatus_rejects_a_transition_the_workflow_forbids()
    {
        var ticket = await CreateTicketAsync();
        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved, null));
        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Closed, null));

        // Closed may only be reopened.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Open, null)));

        Assert.Contains("Closed", ex.Message);
    }

    [Fact]
    public async Task Resolving_then_closing_stamps_both_timestamps()
    {
        var ticket = await CreateTicketAsync();

        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved, null));
        var resolvedAt = _h.Clock.UtcNow;

        _h.Clock.Advance(TimeSpan.FromHours(2));
        var closed = await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Closed, null));

        Assert.Equal(resolvedAt, closed.ResolvedAt);
        Assert.Equal(_h.Clock.UtcNow, closed.ClosedAt);
    }

    [Fact]
    public async Task Closing_directly_still_stamps_a_resolution_time_for_SLA_reporting()
    {
        var ticket = await CreateTicketAsync();

        var closed = await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Closed, null));

        Assert.NotNull(closed.ResolvedAt);
        Assert.Equal(closed.ClosedAt, closed.ResolvedAt);
    }

    [Fact]
    public async Task Reopening_clears_the_previous_resolution()
    {
        var ticket = await CreateTicketAsync();
        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved, null));

        var reopened = await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Reopened, null));

        Assert.Null(reopened.ResolvedAt);
        Assert.Null(reopened.ClosedAt);
        Assert.Equal(TicketStatus.Reopened, reopened.Status);
    }

    [Fact]
    public async Task Assigning_a_new_ticket_also_opens_it()
    {
        var agent = _h.Identity.AddAgent("Sara");
        var ticket = await CreateTicketAsync();

        var assigned = await _h.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(agent.Id, null));

        Assert.Equal(agent.Id, assigned.AssignedAgentId);
        Assert.Equal(TicketStatus.Open, assigned.Status);
        Assert.NotNull(assigned.AssignedAt);
    }

    [Fact]
    public async Task Assigning_rejects_a_user_who_is_not_an_agent()
    {
        var ticket = await CreateTicketAsync();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _h.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(Guid.NewGuid(), null)));
    }

    [Fact]
    public async Task History_shows_agent_names_rather_than_raw_ids()
    {
        var agent = _h.Identity.AddAgent("Sara");
        var ticket = await CreateTicketAsync();
        await _h.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(agent.Id, null));

        var history = await _h.Tickets.GetHistoryAsync(ticket.Id);
        var assignment = history.First(h => h.Field == "AssignedAgentId");

        Assert.Equal("Sara", assignment.NewValue);
    }

    [Fact]
    public async Task A_public_reply_sets_the_first_response_time_but_an_internal_note_does_not()
    {
        var internalTicket = await CreateTicketAsync();
        await _h.Tickets.AddCommentAsync(internalTicket.Id, new CreateTicketCommentRequest("Checking with billing.", IsInternal: true));

        var afterInternal = await _h.Tickets.GetByIdAsync(internalTicket.Id);
        Assert.Null(afterInternal.FirstRespondedAt);
        Assert.Equal(TicketStatus.New, afterInternal.Status);

        await _h.Tickets.AddCommentAsync(internalTicket.Id, new CreateTicketCommentRequest("We are on it.", IsInternal: false));

        var afterPublic = await _h.Tickets.GetByIdAsync(internalTicket.Id);
        Assert.NotNull(afterPublic.FirstRespondedAt);
        Assert.Equal(TicketStatus.Open, afterPublic.Status);
    }

    [Fact]
    public async Task Internal_notes_are_withheld_when_internal_comments_are_not_requested()
    {
        var ticket = await CreateTicketAsync();
        await _h.Tickets.AddCommentAsync(ticket.Id, new CreateTicketCommentRequest("Internal only.", IsInternal: true));
        await _h.Tickets.AddCommentAsync(ticket.Id, new CreateTicketCommentRequest("Customer visible.", IsInternal: false));

        var staffView = await _h.Tickets.GetCommentsAsync(ticket.Id, includeInternal: true);
        var portalView = await _h.Tickets.GetCommentsAsync(ticket.Id, includeInternal: false);

        Assert.Equal(2, staffView.Count);
        Assert.Single(portalView);
        Assert.Equal("Customer visible.", portalView[0].Body);
    }

    [Fact]
    public async Task Update_is_refused_on_a_closed_ticket()
    {
        var ticket = await CreateTicketAsync();
        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Closed, null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _h.Tickets.UpdateAsync(ticket.Id, new UpdateTicketRequest("New subject", "New body", TicketPriority.High, null, null, null)));
    }

    [Fact]
    public async Task Search_defaults_to_most_urgent_first()
    {
        await CreateTicketAsync(TicketPriority.Low);
        await CreateTicketAsync(TicketPriority.Urgent);
        await CreateTicketAsync(TicketPriority.Normal);

        var result = await _h.Tickets.SearchAsync(new TicketQuery());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(TicketPriority.Urgent, result.Items[0].Priority);
        Assert.Equal(TicketPriority.Low, result.Items[^1].Priority);
    }

    [Fact]
    public async Task Search_paging_reports_totals_across_all_pages()
    {
        for (var i = 0; i < 5; i++) await CreateTicketAsync();

        var page = await _h.Tickets.SearchAsync(new TicketQuery { Page = 2, PageSize = 2 });

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasNext);
        Assert.True(page.HasPrevious);
    }

    [Fact]
    public async Task Search_can_isolate_the_unassigned_queue()
    {
        var agent = _h.Identity.AddAgent("Sara");
        var assigned = await CreateTicketAsync();
        await _h.Tickets.AssignAsync(assigned.Id, new AssignTicketRequest(agent.Id, null));
        await CreateTicketAsync();

        var unassigned = await _h.Tickets.SearchAsync(new TicketQuery { Unassigned = true });

        Assert.Single(unassigned.Items);
        Assert.Null(unassigned.Items[0].AssignedAgentId);
    }

    [Fact]
    public async Task Agent_dashboard_counts_only_that_agents_active_work()
    {
        var sara = _h.Identity.AddAgent("Sara");
        var omar = _h.Identity.AddAgent("Omar");

        var first = await CreateTicketAsync(TicketPriority.High);
        var second = await CreateTicketAsync(TicketPriority.Low);
        var third = await CreateTicketAsync();

        await _h.Tickets.AssignAsync(first.Id, new AssignTicketRequest(sara.Id, null));
        await _h.Tickets.AssignAsync(second.Id, new AssignTicketRequest(sara.Id, null));
        await _h.Tickets.AssignAsync(third.Id, new AssignTicketRequest(omar.Id, null));

        await _h.Tickets.ChangeStatusAsync(second.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved, null));

        var board = await _h.Tickets.GetAgentDashboardAsync(sara.Id);

        Assert.Equal(1, board.AssignedActive);
        Assert.Equal(1, board.ResolvedToday);
        Assert.Single(board.RecentAssigned);
        Assert.Equal(first.Id, board.RecentAssigned[0].Id);
    }

    [Fact]
    public async Task Deleted_tickets_disappear_from_search()
    {
        var ticket = await CreateTicketAsync();
        await _h.Tickets.DeleteAsync(ticket.Id);

        var result = await _h.Tickets.SearchAsync(new TicketQuery());

        Assert.Equal(0, result.TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Tickets.GetByIdAsync(ticket.Id));
    }
}
