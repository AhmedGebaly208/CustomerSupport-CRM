using CustomerSupportCRM.Application.Customers;
using CustomerSupportCRM.Application.Customers.Validators;
using CustomerSupportCRM.Infrastructure.Import;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Infrastructure.Persistence;
using CustomerSupportCRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tests.TestSupport;

/// <summary>One isolated in-memory database plus wired-up services per test.
/// The real AuditingInterceptor is included so audit stamping and the audit trail are
/// covered by the same tests that exercise the services.</summary>
public sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        CurrentUser = new FakeCurrentUser();
        Clock = new FakeClock();
        Identity = new FakeIdentityService();
        Numbers = new FakeReferenceNumberGenerator();
        Scope = new FakeScopeProvider();
        Storage = new FakeFileStorage();
        Transactions = new FakeTransactionRunner();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"crm-tests-{Guid.NewGuid()}")
            .AddInterceptors(new AuditingInterceptor(CurrentUser, Clock))
            // The in-memory provider cannot honour relational annotations; the tests here
            // target service behaviour, not SQL generation.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new AppDbContext(options);

        Calendars = new FakeBusinessCalendarProvider();
        Sla = new SlaService(Db, Calendars, Clock);
        Notifications = new NotificationService(Db, CurrentUser, Clock);

        Tickets = new TicketService(
            Db, CurrentUser, Clock, Numbers, Identity, Scope,
            Sla, new AutoAssignmentService(Db, Identity), Notifications);
        Customers = new CustomerService(
            Db, CurrentUser, Clock, Numbers, Identity, Scope,
            Storage, Transactions, [new CsvCustomerImportParser()],
            new CreateCustomerRequestValidator());
    }

    public AppDbContext Db { get; }
    public FakeCurrentUser CurrentUser { get; }
    public FakeClock Clock { get; }
    public FakeIdentityService Identity { get; }
    public FakeReferenceNumberGenerator Numbers { get; }
    public FakeScopeProvider Scope { get; }
    public FakeBusinessCalendarProvider Calendars { get; }
    public ISlaService Sla { get; }
    public INotificationService Notifications { get; }
    public FakeFileStorage Storage { get; }
    public FakeTransactionRunner Transactions { get; }

    public ITicketService Tickets { get; }
    public ICustomerService Customers { get; }

    public Department SeedDepartment(string code = "SUP")
    {
        var department = new Department { Code = code, NameAr = "الدعم", NameEn = "Support" };
        Db.Departments.Add(department);
        Db.SaveChanges();
        return department;
    }

    public TicketCategory SeedCategory(Guid? departmentId = null)
    {
        var category = new TicketCategory { NameAr = "مشكلة تقنية", NameEn = "Technical Issue", DepartmentId = departmentId };
        Db.TicketCategories.Add(category);
        Db.SaveChanges();
        return category;
    }

    public async Task<Customer> SeedCustomerAsync(string nameEn = "Acme Ltd", string? email = null)
    {
        var customer = new Customer
        {
            Code = await Numbers.NextCustomerCodeAsync(),
            FullNameAr = "شركة أكمي",
            FullNameEn = nameEn,
            Email = email ?? $"{Guid.NewGuid():N}@example.com",
            IsActive = true
        };

        Db.Customers.Add(customer);
        await Db.SaveChangesAsync();
        return customer;
    }

    public void Dispose() => Db.Dispose();
}
