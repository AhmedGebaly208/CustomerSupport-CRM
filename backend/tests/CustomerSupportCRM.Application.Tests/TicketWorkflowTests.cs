using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;

namespace CustomerSupportCRM.Application.Tests;

public class TicketWorkflowTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Open)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Pending, TicketStatus.OnHold)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Reopened)]
    [InlineData(TicketStatus.Closed, TicketStatus.Reopened)]
    [InlineData(TicketStatus.Reopened, TicketStatus.Resolved)]
    public void CanTransition_allows_valid_moves(TicketStatus from, TicketStatus to) =>
        Assert.True(TicketWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Pending)]
    public void CanTransition_blocks_invalid_moves(TicketStatus from, TicketStatus to) =>
        Assert.False(TicketWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Closed)]
    public void CanTransition_rejects_a_move_to_the_same_status(TicketStatus status) =>
        Assert.False(TicketWorkflow.CanTransition(status, status));

    [Fact]
    public void Closed_is_terminal_apart_from_reopening()
    {
        var allowed = TicketWorkflow.AllowedTransitions(TicketStatus.Closed);
        Assert.Equal([TicketStatus.Reopened], allowed);
    }

    [Fact]
    public void Active_statuses_exclude_resolved_and_closed()
    {
        Assert.False(TicketWorkflow.IsActive(TicketStatus.Resolved));
        Assert.False(TicketWorkflow.IsActive(TicketStatus.Closed));
        Assert.True(TicketWorkflow.IsActive(TicketStatus.New));
        Assert.True(TicketWorkflow.IsActive(TicketStatus.Reopened));
    }

    [Fact]
    public void Every_status_has_at_least_one_exit()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Assert.NotEmpty(TicketWorkflow.AllowedTransitions(status));
        }
    }

    [Fact]
    public void No_status_lists_itself_as_a_transition()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Assert.DoesNotContain(status, TicketWorkflow.AllowedTransitions(status));
        }
    }
}

public class ReferenceNumberTests
{
    [Fact]
    public void Ticket_numbers_are_zero_padded_to_six_digits() =>
        Assert.Equal("TKT-000042", ReferenceNumber.Ticket(42));

    [Fact]
    public void Customer_codes_are_zero_padded_to_six_digits() =>
        Assert.Equal("CUS-000007", ReferenceNumber.Customer(7));

    [Fact]
    public void Numbers_beyond_six_digits_are_not_truncated() =>
        Assert.Equal("TKT-1234567", ReferenceNumber.Ticket(1_234_567));
}
