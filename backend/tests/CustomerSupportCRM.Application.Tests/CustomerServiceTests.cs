using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Application.Tests.TestSupport;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tests;

public class CustomerServiceTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static CreateCustomerRequest NewCustomer(string nameEn = "Acme Ltd", string? email = "acme@example.com") =>
        new("شركة أكمي", nameEn, email, "0500000000", null, "Acme", null, null, "ar", null, null, null);

    [Fact]
    public async Task Create_allocates_a_sequential_code()
    {
        var first = await _h.Customers.CreateAsync(NewCustomer(email: "one@example.com"));
        var second = await _h.Customers.CreateAsync(NewCustomer(email: "two@example.com"));

        Assert.Equal("CUS-000001", first.Code);
        Assert.Equal("CUS-000002", second.Code);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_email()
    {
        await _h.Customers.CreateAsync(NewCustomer(email: "dup@example.com"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _h.Customers.CreateAsync(NewCustomer("Other Ltd", "dup@example.com")));
    }

    [Fact]
    public async Task Two_customers_may_both_have_no_email()
    {
        await _h.Customers.CreateAsync(NewCustomer("First", email: null));
        var second = await _h.Customers.CreateAsync(NewCustomer("Second", email: null));

        Assert.NotNull(second);
    }

    [Fact]
    public async Task Create_persists_extra_contact_rows()
    {
        var request = NewCustomer() with
        {
            Contacts =
            [
                new SaveCustomerContactRequest(ContactType.WhatsApp, "0555555555", "Owner", true),
                new SaveCustomerContactRequest(ContactType.Email, "billing@acme.com", "Billing", false)
            ]
        };

        var created = await _h.Customers.CreateAsync(request);

        Assert.Equal(2, created.Contacts.Count);
        Assert.Contains(created.Contacts, c => c is { Type: ContactType.WhatsApp, IsPrimary: true });
    }

    [Fact]
    public async Task Update_with_null_contacts_leaves_existing_contacts_untouched()
    {
        var created = await _h.Customers.CreateAsync(NewCustomer() with
        {
            Contacts = [new SaveCustomerContactRequest(ContactType.Mobile, "0511111111", null, true)]
        });

        var updated = await _h.Customers.UpdateAsync(created.Id, new UpdateCustomerRequest(
            "شركة أكمي", "Acme Renamed", created.Email, created.Phone, null, null, null, null,
            "en", null, null, true, Contacts: null));

        Assert.Equal("Acme Renamed", updated.FullNameEn);
        Assert.Single(updated.Contacts);
    }

    [Fact]
    public async Task Update_with_an_empty_contacts_list_clears_them()
    {
        var created = await _h.Customers.CreateAsync(NewCustomer() with
        {
            Contacts = [new SaveCustomerContactRequest(ContactType.Mobile, "0511111111", null, true)]
        });

        var updated = await _h.Customers.UpdateAsync(created.Id, new UpdateCustomerRequest(
            "شركة أكمي", "Acme Ltd", created.Email, created.Phone, null, null, null, null,
            "ar", null, null, true, Contacts: []));

        Assert.Empty(updated.Contacts);
    }

    [Fact]
    public async Task Search_matches_on_arabic_name_english_name_code_and_phone()
    {
        var created = await _h.Customers.CreateAsync(NewCustomer("Globex Trading", "globex@example.com"));

        foreach (var term in new[] { "Globex", "أكمي", created.Code, "050000" })
        {
            var result = await _h.Customers.SearchAsync(new CustomerQuery { Search = term });
            Assert.True(result.TotalCount >= 1, $"Search for '{term}' returned nothing.");
        }
    }

    [Fact]
    public async Task Search_reports_the_open_ticket_count_per_customer()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());

        await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Issue one", "Body", TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null));

        var resolved = await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Issue two", "Body", TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null));

        await _h.Tickets.ChangeStatusAsync(resolved.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved, null));

        var result = await _h.Customers.SearchAsync(new CustomerQuery());

        // Only the still-active ticket counts.
        Assert.Equal(1, result.Items.Single().OpenTicketCount);
    }

    [Fact]
    public async Task PageSize_is_clamped_rather_than_honoured_verbatim()
    {
        var query = new CustomerQuery { PageSize = 5000 };
        Assert.Equal(100, query.PageSize);

        query.PageSize = 0;
        Assert.Equal(20, query.PageSize);
    }

    [Fact]
    public async Task Delete_is_refused_while_the_customer_has_an_active_ticket()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());
        await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Open issue", "Body", TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => _h.Customers.DeleteAsync(customer.Id));
    }

    [Fact]
    public async Task Delete_succeeds_once_every_ticket_is_closed()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());
        var ticket = await _h.Tickets.CreateAsync(new CreateTicketRequest(
            customer.Id, "Open issue", "Body", TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null));

        await _h.Tickets.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Closed, null));
        await _h.Customers.DeleteAsync(customer.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _h.Customers.GetByIdAsync(customer.Id));
    }

    [Fact]
    public async Task Interactions_are_ordered_newest_first()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());

        await _h.Customers.AddInteractionAsync(customer.Id, new CreateInteractionRequest(
            CommunicationChannel.Phone, InteractionDirection.Inbound, "First call", "Body",
            _h.Clock.UtcNow.AddHours(-2), null));

        await _h.Customers.AddInteractionAsync(customer.Id, new CreateInteractionRequest(
            CommunicationChannel.WhatsApp, InteractionDirection.Outbound, "Follow up", "Body",
            _h.Clock.UtcNow, null));

        var page = await _h.Customers.GetInteractionsAsync(customer.Id, 1, 20);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Follow up", page.Items[0].Subject);
    }

    [Fact]
    public async Task An_interaction_cannot_be_attached_to_another_customers_ticket()
    {
        var owner = await _h.Customers.CreateAsync(NewCustomer("Owner", "owner@example.com"));
        var other = await _h.Customers.CreateAsync(NewCustomer("Other", "other@example.com"));

        var ticket = await _h.Tickets.CreateAsync(new CreateTicketRequest(
            owner.Id, "Theirs", "Body", TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _h.Customers.AddInteractionAsync(other.Id, new CreateInteractionRequest(
                CommunicationChannel.Email, InteractionDirection.Inbound, null, "Body", null, ticket.Id)));
    }

    [Fact]
    public async Task Notes_capture_the_author_and_are_soft_deleted()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());
        var note = await _h.Customers.AddNoteAsync(customer.Id, new CreateCustomerNoteRequest("Prefers Arabic.", true));

        Assert.Equal(_h.CurrentUser.UserId, note.CreatedBy);

        await _h.Customers.DeleteNoteAsync(customer.Id, note.Id);

        Assert.Empty(await _h.Customers.GetNotesAsync(customer.Id));
    }

    [Fact]
    public async Task The_interceptor_stamps_audit_columns_and_writes_an_audit_trail()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());

        var entity = await _h.Db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(_h.Clock.UtcNow, entity.CreatedAt);
        Assert.Equal(_h.CurrentUser.UserId, entity.CreatedBy);

        var created = await _h.Db.AuditLogs
            .SingleAsync(a => a.EntityName == "Customer" && a.Action == AuditAction.Created);
        Assert.Equal(customer.Id.ToString(), created.EntityId);

        _h.Clock.Advance(TimeSpan.FromMinutes(30));
        await _h.Customers.UpdateAsync(customer.Id, new UpdateCustomerRequest(
            "شركة أكمي", "Acme Updated", customer.Email, customer.Phone, null, null, null, null,
            "ar", null, null, true, null));

        var updated = await _h.Db.AuditLogs
            .SingleAsync(a => a.EntityName == "Customer" && a.Action == AuditAction.Updated);

        Assert.NotNull(updated.Changes);
        Assert.Contains("FullNameEn", updated.Changes);
    }

    [Fact]
    public async Task A_soft_delete_is_audited_as_a_deletion_not_an_update()
    {
        var customer = await _h.Customers.CreateAsync(NewCustomer());
        await _h.Customers.DeleteAsync(customer.Id);

        var deletion = await _h.Db.AuditLogs
            .SingleAsync(a => a.EntityName == "Customer" && a.Action == AuditAction.Deleted);

        Assert.Equal(customer.Id.ToString(), deletion.EntityId);
    }
}
