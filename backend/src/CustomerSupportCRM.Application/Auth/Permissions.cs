namespace CustomerSupportCRM.Application.Auth;

/// <summary>Named permissions — the unit of authorization (PDF area 10 "Permissions").
///
/// Endpoints authorise against these rather than role names, so a future story can make
/// roles admin-editable without touching a single <c>[Authorize]</c> attribute. The values
/// travel as <c>"permission"</c> claims on the JWT, which means the caller's permission set
/// is cached until the access token expires — that is why the access-token lifetime is kept
/// to 15 minutes.</summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Customers
    {
        public const string View = "customers.view";
        public const string Create = "customers.create";
        public const string Edit = "customers.edit";
        public const string Delete = "customers.delete";

        /// <summary>Folding one customer record into another. Irreversible in practice, so
        /// it is supervisory rather than part of ordinary editing.</summary>
        public const string Merge = "customers.merge";

        /// <summary>Bulk import from a spreadsheet.</summary>
        public const string Import = "customers.import";
    }

    public static class Attachments
    {
        public const string View = "attachments.view";
        public const string Upload = "attachments.upload";
        public const string Delete = "attachments.delete";
    }

    public static class Tickets
    {
        public const string View = "tickets.view";
        public const string Create = "tickets.create";
        public const string Edit = "tickets.edit";
        public const string Assign = "tickets.assign";
        public const string Comment = "tickets.comment";

        /// <summary>Seeing internal, agent-only notes on a ticket. A portal customer holds
        /// <see cref="View"/> and <see cref="Comment"/> but never this.</summary>
        public const string ViewInternal = "tickets.viewinternal";
        public const string Close = "tickets.close";
        public const string Delete = "tickets.delete";
    }

    public static class Dashboard
    {
        public const string View = "dashboard.view";

        /// <summary>Viewing another agent's dashboard, i.e. a supervisor's team view.</summary>
        public const string ViewTeam = "dashboard.viewteam";
    }

    /// <summary>Service-level agreements (PDF area 5). Viewing a ticket's SLA state needs no
    /// permission of its own — it rides on tickets.view, because a badge the agent cannot see
    /// is a target they cannot meet. Only editing the policies is gated.</summary>
    public static class Sla
    {
        public const string View = "sla.view";
        public const string Manage = "sla.manage";
    }

    /// <summary>The knowledge base (PDF area 6). Reading is separate from authoring because
    /// every agent consults it while only some write it.</summary>
    public static class KnowledgeBase
    {
        public const string View = "kb.view";
        public const string Manage = "kb.manage";
        public const string Publish = "kb.publish";
    }

    /// <summary>Historical reporting (PDF area 8). Separate from the live dashboards: a
    /// dashboard shows an agent their own queue, a report shows the desk's history.</summary>
    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    /// <summary>The customer portal (PDF area 9).
    ///
    /// Deliberately separate from the staff ticket permissions rather than reusing them. A
    /// portal customer holding tickets.view would be one misconfigured department claim away
    /// from reading the desk's queue; holding only portal.* they cannot reach a staff
    /// endpoint at all, whatever their scope says.</summary>
    public static class Portal
    {
        public const string Access = "portal.access";
        public const string CreateTicket = "portal.ticket.create";
        public const string Reply = "portal.ticket.reply";
    }

    public static class Lookups
    {
        public const string View = "lookups.view";
        public const string Manage = "lookups.manage";
    }

    public static class AuditLogs
    {
        public const string View = "auditlogs.view";
    }

    public static class SystemConfig
    {
        public const string View = "systemconfig.view";
        public const string Manage = "systemconfig.manage";
    }

    /// <summary>Every permission the system knows about. The policy provider uses this to
    /// decide whether a policy name is a permission or should fall through to the default
    /// provider, so an unlisted permission silently never authorises anyone.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Users.View, Users.Manage,
        Customers.View, Customers.Create, Customers.Edit, Customers.Delete,
        Customers.Merge, Customers.Import,
        Attachments.View, Attachments.Upload, Attachments.Delete,
        Tickets.View, Tickets.Create, Tickets.Edit, Tickets.Assign, Tickets.Comment,
        Tickets.ViewInternal, Tickets.Close, Tickets.Delete,
        Dashboard.View, Dashboard.ViewTeam,
        Sla.View, Sla.Manage,
        Reports.View, Reports.Export,
        Portal.Access, Portal.CreateTicket, Portal.Reply,
        KnowledgeBase.View, KnowledgeBase.Manage, KnowledgeBase.Publish,
        Lookups.View, Lookups.Manage,
        AuditLogs.View,
        SystemConfig.View, SystemConfig.Manage
    ];
}
