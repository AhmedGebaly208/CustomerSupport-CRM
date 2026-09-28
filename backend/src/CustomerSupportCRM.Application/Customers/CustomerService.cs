using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Tickets;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Customers;

public sealed partial class CustomerService : ICustomerService
{
    private readonly IAppDbContext db;
    private readonly ICurrentUser currentUser;
    private readonly IClock clock;
    private readonly IReferenceNumberGenerator numbers;
    private readonly IIdentityService identity;
    private readonly IScopeProvider scope;
    private readonly IFileStorage storage;
    private readonly ITransactionRunner transactions;
    private readonly IEnumerable<ICustomerImportParser> importParsers;
    private readonly IValidator<CreateCustomerRequest> importValidator;

    public CustomerService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IReferenceNumberGenerator numbers,
        IIdentityService identity,
        IScopeProvider scope,
        IFileStorage storage,
        ITransactionRunner transactions,
        IEnumerable<ICustomerImportParser> importParsers,
        IValidator<CreateCustomerRequest> importValidator)
    {
        this.db = db;
        this.currentUser = currentUser;
        this.clock = clock;
        this.numbers = numbers;
        this.identity = identity;
        this.scope = scope;
        this.storage = storage;
        this.transactions = transactions;
        this.importParsers = importParsers;
        this.importValidator = importValidator;
    }

    /// <summary>Shorthand so the extended half reads cleanly.</summary>
    private Task<T> RunInTransactionAsync<T>(Func<Task<T>> work) => transactions.RunAsync(work);

    public async Task<PagedResult<CustomerListItemDto>> SearchAsync(CustomerQuery query, CancellationToken ct = default)
    {
        // Scope first, so no later filter can widen it back out.
        var q = scope.Apply(db.Customers.AsNoTracking());

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // One box searches both languages plus the identifiers an agent is most
            // likely to have on hand from an inbound call.
            var term = query.Search.Trim();
            q = q.Where(c =>
                c.FullNameAr.Contains(term) ||
                c.FullNameEn.Contains(term) ||
                c.Code.Contains(term) ||
                (c.Email != null && c.Email.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.WhatsAppNumber != null && c.WhatsAppNumber.Contains(term)) ||
                (c.CompanyName != null && c.CompanyName.Contains(term)));
        }

        if (query.DepartmentId is { } departmentId) q = q.Where(c => c.DepartmentId == departmentId);
        if (query.BranchId is { } branchId) q = q.Where(c => c.BranchId == branchId);
        if (query.IsActive is { } isActive) q = q.Where(c => c.IsActive == isActive);

        var total = await q.CountAsync(ct);

        q = ApplySort(q, query.SortBy, query.SortDescending);

        var activeStatuses = TicketWorkflow.ActiveStatuses;

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(c => new CustomerListItemDto(
                c.Id,
                c.Code,
                c.FullNameAr,
                c.FullNameEn,
                c.Email,
                c.Phone,
                c.CompanyName,
                c.Department != null ? c.Department.NameAr : null,
                c.Department != null ? c.Department.NameEn : null,
                c.Branch != null ? c.Branch.NameAr : null,
                c.Branch != null ? c.Branch.NameEn : null,
                c.IsActive,
                c.Tickets.Count(t => activeStatuses.Contains(t.Status)),
                c.CreatedAt))
            .ToListAsync(ct);

        return PagedResult<CustomerListItemDto>.Create(items, total, query.Page, query.PageSize);
    }

    private static IQueryable<Customer> ApplySort(IQueryable<Customer> q, string? sortBy, bool desc) =>
        sortBy?.ToLowerInvariant() switch
        {
            "code" => desc ? q.OrderByDescending(c => c.Code) : q.OrderBy(c => c.Code),
            "namear" => desc ? q.OrderByDescending(c => c.FullNameAr) : q.OrderBy(c => c.FullNameAr),
            "nameen" => desc ? q.OrderByDescending(c => c.FullNameEn) : q.OrderBy(c => c.FullNameEn),
            "email" => desc ? q.OrderByDescending(c => c.Email) : q.OrderBy(c => c.Email),
            "createdat" => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            // Newest first is the useful default for a support desk.
            _ => q.OrderByDescending(c => c.CreatedAt)
        };

    public async Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .Include(c => c.Department)
            .Include(c => c.Branch)
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        scope.EnsureCanAccess(customer);
        return ToDetail(customer);
    }

    public async Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        await GuardDuplicateEmailAsync(request.Email, null, ct);
        await GuardLookupsAsync(request.DepartmentId, request.BranchId, ct);

        var customer = new Customer
        {
            Code = await numbers.NextCustomerCodeAsync(ct),
            FullNameAr = request.FullNameAr.Trim(),
            FullNameEn = request.FullNameEn.Trim(),
            Email = Normalize(request.Email),
            Phone = Normalize(request.Phone),
            WhatsAppNumber = Normalize(request.WhatsAppNumber),
            CompanyName = Normalize(request.CompanyName),
            NationalId = Normalize(request.NationalId),
            Address = Normalize(request.Address),
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "ar" : request.PreferredLanguage,
            DepartmentId = request.DepartmentId,
            BranchId = request.BranchId,
            IsActive = true
        };

        AddContacts(customer, request.Contacts);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(customer.Id, ct);
    }

    public async Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        scope.EnsureCanAccess(customer);
        await GuardDuplicateEmailAsync(request.Email, id, ct);
        await GuardLookupsAsync(request.DepartmentId, request.BranchId, ct);

        customer.FullNameAr = request.FullNameAr.Trim();
        customer.FullNameEn = request.FullNameEn.Trim();
        customer.Email = Normalize(request.Email);
        customer.Phone = Normalize(request.Phone);
        customer.WhatsAppNumber = Normalize(request.WhatsAppNumber);
        customer.CompanyName = Normalize(request.CompanyName);
        customer.NationalId = Normalize(request.NationalId);
        customer.Address = Normalize(request.Address);
        customer.PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "ar" : request.PreferredLanguage;
        customer.DepartmentId = request.DepartmentId;
        customer.BranchId = request.BranchId;
        customer.IsActive = request.IsActive;

        // A null Contacts collection means "leave contacts alone"; an empty one means
        // "remove them all". Treating both the same would make partial updates lossy.
        if (request.Contacts is not null)
        {
            db.CustomerContacts.RemoveRange(customer.Contacts);
            customer.Contacts.Clear();
            AddContacts(customer, request.Contacts);
        }

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(customer.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        scope.EnsureCanAccess(customer);

        // Refuse to remove a customer who still has live tickets: doing so would orphan
        // work an agent is actively handling.
        var activeStatuses = TicketWorkflow.ActiveStatuses;
        var openTickets = await db.Tickets.CountAsync(t => t.CustomerId == id && activeStatuses.Contains(t.Status), ct);
        if (openTickets > 0)
            throw new ConflictException($"Customer has {openTickets} active ticket(s) and cannot be deleted.");

        customer.IsDeleted = true;
        customer.DeletedAt = clock.UtcNow;
        customer.DeletedBy = currentUser.UserId;
        await db.SaveChangesAsync(ct);
    }

    // ---- Notes ----

    public async Task<IReadOnlyList<CustomerNoteDto>> GetNotesAsync(Guid customerId, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);

        var notes = await db.CustomerNotes.AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(notes.Select(n => n.CreatedBy), ct);

        return notes
            .Select(n => new CustomerNoteDto(n.Id, n.Body, n.IsInternal, n.CreatedBy, Lookup(names, n.CreatedBy), n.CreatedAt))
            .ToList();
    }

    public async Task<CustomerNoteDto> AddNoteAsync(Guid customerId, CreateCustomerNoteRequest request, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);

        var note = new CustomerNote
        {
            CustomerId = customerId,
            Body = request.Body.Trim(),
            IsInternal = request.IsInternal
        };

        db.CustomerNotes.Add(note);
        await db.SaveChangesAsync(ct);

        var names = await ResolveNamesAsync([note.CreatedBy], ct);
        return new CustomerNoteDto(note.Id, note.Body, note.IsInternal, note.CreatedBy, Lookup(names, note.CreatedBy), note.CreatedAt);
    }

    public async Task DeleteNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default)
    {
        var note = await db.CustomerNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId, ct)
            ?? throw new NotFoundException(nameof(CustomerNote), noteId);

        note.IsDeleted = true;
        note.DeletedAt = clock.UtcNow;
        note.DeletedBy = currentUser.UserId;
        await db.SaveChangesAsync(ct);
    }

    // ---- Interaction history ----

    public async Task<PagedResult<InteractionDto>> GetInteractionsAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;

        var q = db.Interactions.AsNoTracking().Where(i => i.CustomerId == customerId);
        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(i => i.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.CustomerId,
                i.TicketId,
                TicketNumber = i.Ticket != null ? i.Ticket.Number : null,
                i.Channel,
                i.Direction,
                i.Subject,
                i.Body,
                i.OccurredAt,
                i.AgentId
            })
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(rows.Select(r => r.AgentId), ct);

        var items = rows
            .Select(r => new InteractionDto(
                r.Id, r.CustomerId, r.TicketId, r.TicketNumber, r.Channel, r.Direction,
                r.Subject, r.Body, r.OccurredAt, r.AgentId, Lookup(names, r.AgentId)))
            .ToList();

        return PagedResult<InteractionDto>.Create(items, total, page, pageSize);
    }

    public async Task<InteractionDto> AddInteractionAsync(Guid customerId, CreateInteractionRequest request, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);

        if (request.TicketId is { } ticketId)
        {
            var belongsToCustomer = await db.Tickets.AnyAsync(t => t.Id == ticketId && t.CustomerId == customerId, ct);
            if (!belongsToCustomer)
                throw new BadRequestException("The ticket does not belong to this customer.");
        }

        var interaction = new Interaction
        {
            CustomerId = customerId,
            TicketId = request.TicketId,
            Channel = request.Channel,
            Direction = request.Direction,
            Subject = Normalize(request.Subject),
            Body = request.Body.Trim(),
            // Agents log calls after the fact, so an explicit time wins over "now".
            OccurredAt = request.OccurredAt ?? clock.UtcNow,
            AgentId = currentUser.UserId
        };

        db.Interactions.Add(interaction);
        await db.SaveChangesAsync(ct);

        var ticketNumber = interaction.TicketId is null
            ? null
            : await db.Tickets.Where(t => t.Id == interaction.TicketId).Select(t => t.Number).FirstOrDefaultAsync(ct);

        var names = await ResolveNamesAsync([interaction.AgentId], ct);

        return new InteractionDto(
            interaction.Id, customerId, interaction.TicketId, ticketNumber, interaction.Channel,
            interaction.Direction, interaction.Subject, interaction.Body, interaction.OccurredAt,
            interaction.AgentId, Lookup(names, interaction.AgentId));
    }

    // ---- helpers ----

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Lookup(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } key && names.TryGetValue(key, out var name) ? name : null;

    private Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct) =>
        identity.GetUserDisplayNamesAsync(ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct(), ct);

    /// <summary>The single scope checkpoint for every child collection (notes,
    /// interactions, attachments): they are only ever reached through their customer, so
    /// gating the parent here gates all of them.</summary>
    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.DepartmentId, c.BranchId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Customer), customerId);

        scope.EnsureCanAccess(new ScopeCheck(customer.DepartmentId, customer.BranchId));
    }

    /// <summary>Lightweight carrier so a projection can be scope-checked without loading
    /// the whole entity.</summary>
    private sealed record ScopeCheck(Guid? DepartmentId, Guid? BranchId) : IScopedEntity;

    private async Task GuardDuplicateEmailAsync(string? email, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var normalized = email.Trim();
        var clash = await db.Customers
            .AnyAsync(c => c.Email == normalized && (excludeId == null || c.Id != excludeId), ct);

        if (clash)
            throw new ConflictException($"Another customer already uses the email '{normalized}'.");
    }

    private async Task GuardLookupsAsync(Guid? departmentId, Guid? branchId, CancellationToken ct)
    {
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct))
            throw new BadRequestException("The selected department does not exist.");

        if (branchId is { } b && !await db.Branches.AnyAsync(x => x.Id == b, ct))
            throw new BadRequestException("The selected branch does not exist.");
    }

    private static void AddContacts(Customer customer, IReadOnlyList<SaveCustomerContactRequest>? contacts)
    {
        if (contacts is null) return;

        foreach (var contact in contacts.Where(c => !string.IsNullOrWhiteSpace(c.Value)))
        {
            customer.Contacts.Add(new CustomerContact
            {
                CustomerId = customer.Id,
                Type = contact.Type,
                Value = contact.Value.Trim(),
                Label = Normalize(contact.Label),
                IsPrimary = contact.IsPrimary
            });
        }
    }

    private static CustomerDetailDto ToDetail(Customer c) => new(
        c.Id, c.Code, c.FullNameAr, c.FullNameEn, c.Email, c.Phone, c.WhatsAppNumber,
        c.CompanyName, c.NationalId, c.Address, c.PreferredLanguage ?? "ar",
        c.DepartmentId, c.Department?.NameAr, c.Department?.NameEn,
        c.BranchId, c.Branch?.NameAr, c.Branch?.NameEn,
        c.IsActive, c.CreatedAt, c.ModifiedAt,
        c.Contacts.Select(x => new CustomerContactDto(x.Id, x.Type, x.Value, x.Label, x.IsPrimary)).ToList());
}
