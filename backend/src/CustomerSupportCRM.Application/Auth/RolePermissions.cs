namespace CustomerSupportCRM.Application.Auth;

/// <summary>Maps the four shipped roles onto permission sets.
///
/// These sets are derived from what each role could reach *before* the permission model
/// existed, so the migration off <c>[Authorize(Roles = …)]</c> changes no endpoint's
/// reachable audience:
///
/// <list type="bullet">
/// <item>Staff (Admin, Manager, Agent) reached customers, tickets, lookups, agents and the
/// dashboard, and saw internal ticket notes.</item>
/// <item>Supervisory (Admin, Manager) additionally reached the delete endpoints and another
/// agent's dashboard.</item>
/// <item>Admin alone reached user administration.</item>
/// <item>Customer reached nothing server-side yet; its set is scoped to what the customer
/// portal story will need, and deliberately excludes <c>tickets.viewinternal</c>.</item>
/// </list>
///
/// Hardcoded on purpose: a follow-up story can move the mapping to a table, and every
/// <c>[Authorize]</c> attribute will keep working unchanged when it does.</summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Map =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Admin is the union of everything, so a new permission is never accidentally
            // unreachable by anyone.
            [Roles.Admin] = [.. Permissions.All],

            [Roles.Manager] =
            [
                Permissions.Users.View,

                Permissions.Customers.View, Permissions.Customers.Create,
                Permissions.Customers.Edit, Permissions.Customers.Delete,
                Permissions.Customers.Merge, Permissions.Customers.Import,

                Permissions.Attachments.View, Permissions.Attachments.Upload,
                Permissions.Attachments.Delete,

                Permissions.Tickets.View, Permissions.Tickets.Create, Permissions.Tickets.Edit,
                Permissions.Tickets.Assign, Permissions.Tickets.Comment,
                Permissions.Tickets.ViewInternal, Permissions.Tickets.Close,
                Permissions.Tickets.Delete,

                Permissions.Dashboard.View, Permissions.Dashboard.ViewTeam,

                Permissions.Sla.View, Permissions.Sla.Manage,

                Permissions.Reports.View, Permissions.Reports.Export,

                Permissions.KnowledgeBase.View, Permissions.KnowledgeBase.Manage,
                Permissions.KnowledgeBase.Publish,

                Permissions.Lookups.View, Permissions.Lookups.Manage,
                Permissions.AuditLogs.View,
                Permissions.SystemConfig.View
            ],

            [Roles.Agent] =
            [
                Permissions.Customers.View, Permissions.Customers.Create, Permissions.Customers.Edit,

                // Agents attach and read files as part of ordinary work, but deleting one
                // removes evidence from a ticket, so that stays supervisory.
                Permissions.Attachments.View, Permissions.Attachments.Upload,

                Permissions.Tickets.View, Permissions.Tickets.Create, Permissions.Tickets.Edit,
                Permissions.Tickets.Assign, Permissions.Tickets.Comment,
                Permissions.Tickets.ViewInternal, Permissions.Tickets.Close,

                Permissions.Dashboard.View,

                // Agents read the targets they are measured against but do not set them.
                Permissions.Sla.View,

                // Agents consult the knowledge base and draft articles from what they learn
                // on a ticket, but publishing is an editorial decision.
                Permissions.KnowledgeBase.View, Permissions.KnowledgeBase.Manage,

                Permissions.Lookups.View
            ],

            // Portal customers: read and reply on their own tickets only. The portal story
            // scopes the data; this set keeps internal notes out of reach regardless.
            [Roles.Customer] =
            [
                Permissions.Tickets.View,
                Permissions.Tickets.Create,
                Permissions.Tickets.Comment,

                // Portal self-service. The service filters to published, public articles.
                Permissions.KnowledgeBase.View,

                // A portal customer sees files on their own tickets; the portal story
                // scopes which tickets those are.
                Permissions.Attachments.View,
                Permissions.Attachments.Upload
            ]
        };

    public static IReadOnlySet<string> For(string role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : new HashSet<string>();

    /// <summary>The union across every role the user holds.</summary>
    public static IReadOnlySet<string> ForRoles(IEnumerable<string> roles)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            union.UnionWith(For(role));
        }

        return union;
    }
}
