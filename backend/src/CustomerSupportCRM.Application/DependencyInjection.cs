using System.Reflection;
using CustomerSupportCRM.Application.AuditLogs;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Customers;
using CustomerSupportCRM.Application.Lookups;
using CustomerSupportCRM.Application.SavedViews;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Application.SystemConfig;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Workspace;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCRM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ITicketCategoryService, TicketCategoryService>();
        services.AddScoped<ISavedViewService, SavedViewService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ISystemConfigService, SystemConfigService>();

        services.AddScoped<ISlaService, SlaService>();
        services.AddScoped<ISlaPolicyService, SlaPolicyService>();
        services.AddScoped<IAutoAssignmentService, AutoAssignmentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISlaEvaluator, SlaEvaluator>();

        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IReminderDispatcher, ReminderDispatcher>();

        services.AddSingleton<IClock, SystemClock>();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);

        return services;
    }
}
