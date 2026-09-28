// Mirrors the DTOs in CustomerSupportCRM.Application. The enum values match the C#
// enums exactly; /api/lookups/enums is the server's own copy of these lists, so a drift
// here shows up as a mismatch at runtime rather than silently.

export enum TicketStatus {
  New = 0,
  Open = 1,
  Pending = 2,
  OnHold = 3,
  Resolved = 4,
  Closed = 5,
  Reopened = 6,
}

export enum TicketPriority {
  Low = 0,
  Normal = 1,
  High = 2,
  Urgent = 3,
}

export enum CommunicationChannel {
  Email = 0,
  WhatsApp = 1,
  LiveChat = 2,
  Sms = 3,
  WebForm = 4,
  Portal = 5,
  Phone = 6,
  Internal = 7,
}

export enum ContactType {
  Email = 0,
  Mobile = 1,
  Phone = 2,
  WhatsApp = 3,
  Fax = 4,
  Other = 5,
}

export enum InteractionDirection {
  Inbound = 0,
  Outbound = 1,
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

// ---- Auth ----

export interface CurrentUser {
  id: string
  email: string
  fullNameAr: string
  fullNameEn: string
  preferredLanguage: string
  departmentId: string | null
  branchId: string | null
  roles: string[]
  /** Effective permissions, supplied by the server so the UI cannot disagree with the API. */
  permissions: string[]
}

/** Mirrors Application/Auth/Permissions.cs. Drives route guards and menu visibility. */
// ---- SLA and automation (area 5) ----

export enum SlaTargetKind {
  FirstResponse = 0,
  Resolution = 1,
}

export enum SlaState {
  /** No policy matched, so nothing is being measured. */
  None = 0,
  Met = 1,
  Running = 2,
  /** Past the warning threshold but still inside the target. */
  AtRisk = 3,
  Breached = 4,
  /** Stopped because the ticket sits in a paused status. */
  Paused = 5,
}

export enum AutoAssignmentStrategy {
  None = 0,
  LeastBusy = 1,
  RoundRobin = 2,
}

export enum NotificationKind {
  TicketAssigned = 0,
  TicketEscalated = 1,
  SlaAtRisk = 2,
  SlaBreached = 3,
  TicketCommented = 4,
  Mention = 5,
}

export interface SlaTarget {
  priority: TicketPriority
  firstResponseMinutes: number
  resolutionMinutes: number
}

export interface SlaEscalationRule {
  id: string
  nameAr: string
  nameEn: string
  isActive: boolean
  target: SlaTargetKind
  thresholdPercent: number
  raiseLevelBy: number
  reassignToUserId: string | null
  reassignToName: string | null
  notifyRole: string | null
}

export interface SlaPolicy {
  id: string
  nameAr: string
  nameEn: string
  isActive: boolean
  rank: number
  departmentId: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchId: string | null
  branchNameAr: string | null
  branchNameEn: string | null
  categoryId: string | null
  categoryNameAr: string | null
  categoryNameEn: string | null
  countsBusinessHoursOnly: boolean
  pausedStatuses: TicketStatus[]
  assignmentStrategy: AutoAssignmentStrategy
  targets: SlaTarget[]
  escalationRules: SlaEscalationRule[]
}

export interface SaveSlaPolicyRequest {
  nameAr: string
  nameEn: string
  isActive: boolean
  rank: number
  departmentId: string | null
  branchId: string | null
  categoryId: string | null
  countsBusinessHoursOnly: boolean
  pausedStatuses: TicketStatus[]
  assignmentStrategy: AutoAssignmentStrategy
  targets: SlaTarget[]
}

export interface SaveSlaEscalationRuleRequest {
  nameAr: string
  nameEn: string
  isActive: boolean
  target: SlaTargetKind
  thresholdPercent: number
  raiseLevelBy: number
  reassignToUserId: string | null
  notifyRole: string | null
}

export interface SlaClock {
  kind: SlaTargetKind
  state: SlaState
  dueAt: string | null
  targetMinutes: number | null
  elapsedMinutes: number | null
  percentConsumed: number | null
  /** Negative once the target is missed. */
  remainingMinutes: number | null
}

export interface TicketSlaStatus {
  ticketId: string
  policyId: string | null
  policyNameAr: string | null
  policyNameEn: string | null
  escalationLevel: number
  firstResponse: SlaClock
  resolution: SlaClock
}

export interface SlaPreviewRequest {
  policyId: string
  priority: TicketPriority
  startAt: string | null
}

export interface SlaPreview {
  startAt: string
  firstResponseDueAt: string
  resolutionDueAt: string
  countsBusinessHoursOnly: boolean
}

/** The event, not its wording — the client renders it in the reader's language. */
export interface AppNotification {
  id: string
  kind: NotificationKind
  parameters: Record<string, string> | null
  ticketId: string | null
  ticketNumber: string | null
  createdAt: string
  readAt: string | null
}

export interface NotificationList {
  items: AppNotification[]
  unreadCount: number
}

// ---- Agent workspace (area 4) ----

export interface AgentTask {
  id: string
  title: string
  notes: string | null
  ownerUserId: string
  ownerName: string | null
  dueAt: string | null
  isDone: boolean
  completedAt: string | null
  isReminder: boolean
  ticketId: string | null
  ticketNumber: string | null
  customerId: string | null
  customerName: string | null
  createdAt: string
}

export interface SaveAgentTaskRequest {
  title: string
  notes: string | null
  dueAt: string | null
  isReminder: boolean
  ticketId: string | null
  customerId: string | null
}

export interface QuickReply {
  id: string
  titleAr: string
  titleEn: string
  bodyAr: string
  bodyEn: string
  shortcut: string | null
  isActive: boolean
  departmentId: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchId: string | null
}

export interface SaveQuickReplyRequest {
  titleAr: string
  titleEn: string
  bodyAr: string
  bodyEn: string
  shortcut: string | null
  isActive: boolean
  departmentId: string | null
  branchId: string | null
}

export interface TeamMemberLoad {
  agentId: string
  agentNameAr: string
  agentNameEn: string
  departmentId: string | null
  active: number
  overdue: number
  atRisk: number
  resolvedToday: number
}

export interface TeamDashboard {
  totalActive: number
  totalUnassigned: number
  totalOverdue: number
  totalAtRisk: number
  resolvedToday: number
  byStatus: Record<number, number>
  byPriority: Record<number, number>
  members: TeamMemberLoad[]
  oldestUnassigned: TicketListItem[]
}

export interface AgentWorkspace {
  tickets: AgentDashboard
  openTasks: AgentTask[]
  overdueTaskCount: number
  mentionedTickets: TicketListItem[]
  watchedTickets: TicketListItem[]
}

// ---- Knowledge base (area 6) ----

export enum ArticleStatus {
  Draft = 0,
  Published = 1,
  Archived = 2,
}

export interface ArticleCategory {
  id: string
  parentId: string | null
  nameAr: string
  nameEn: string
  sortOrder: number
  isActive: boolean
  departmentId: string | null
  articleCount: number
  children: ArticleCategory[]
}

export interface SaveArticleCategoryRequest {
  parentId: string | null
  nameAr: string
  nameEn: string
  sortOrder: number
  isActive: boolean
  departmentId: string | null
  branchId: string | null
}

export interface ArticleTag {
  slug: string
  labelAr: string
  labelEn: string
}

export interface ArticleListItem {
  id: string
  categoryId: string
  categoryNameAr: string
  categoryNameEn: string
  titleAr: string
  titleEn: string
  slugAr: string
  slugEn: string
  summaryAr: string | null
  summaryEn: string | null
  status: ArticleStatus
  isFaq: boolean
  isPublic: boolean
  viewCount: number
  helpfulCount: number
  notHelpfulCount: number
  publishedAt: string | null
  modifiedAt: string | null
  tags: ArticleTag[]
  excerpt: string | null
}

export interface ArticleDetail {
  id: string
  categoryId: string
  categoryNameAr: string
  categoryNameEn: string
  titleAr: string
  titleEn: string
  slugAr: string
  slugEn: string
  summaryAr: string | null
  summaryEn: string | null
  bodyAr: string
  bodyEn: string
  status: ArticleStatus
  isFaq: boolean
  isPublic: boolean
  authorUserId: string | null
  authorName: string | null
  viewCount: number
  helpfulCount: number
  notHelpfulCount: number
  publishedAt: string | null
  firstPublishedAt: string | null
  createdAt: string
  modifiedAt: string | null
  departmentId: string | null
  branchId: string | null
  tags: ArticleTag[]
  slugsLocked: boolean
}

export interface SaveArticleRequest {
  categoryId: string
  titleAr: string
  titleEn: string
  summaryAr: string | null
  summaryEn: string | null
  bodyAr: string
  bodyEn: string
  isFaq: boolean
  isPublic: boolean
  departmentId: string | null
  branchId: string | null
  tags: ArticleTag[] | null
  changeNote: string | null
}

export interface ArticleVersion {
  id: string
  versionNumber: number
  titleAr: string
  titleEn: string
  status: ArticleStatus
  editorUserId: string | null
  editorName: string | null
  editedAt: string
  changeNote: string | null
}

export interface ArticleVoteResult {
  helpfulCount: number
  notHelpfulCount: number
  yourVote: boolean
}

export interface ArticleTicketLink {
  id: string
  articleId: string
  titleAr: string
  titleEn: string
  slugAr: string
  slugEn: string
  linkedByUserId: string | null
  linkedByName: string | null
  linkedAt: string
}

export interface ArticleSearchResult {
  results: PagedResult<ArticleListItem>
  availableTags: ArticleTag[]
}

// ---- Communication channels (area 3) ----

export enum ChannelDirection {
  Inbound = 0,
  Outbound = 1,
}

export enum ChannelMessageStatus {
  Pending = 0,
  Sent = 1,
  Delivered = 2,
  Retrying = 3,
  Failed = 4,
}

export interface ChannelStatus {
  channel: CommunicationChannel
  /** An adapter is compiled into this build; a channel without one cannot be switched on. */
  hasAdapter: boolean
  isEnabled: boolean
}

export interface ChannelMessage {
  id: string
  channel: CommunicationChannel
  direction: ChannelDirection
  address: string | null
  subject: string | null
  bodyText: string
  status: ChannelMessageStatus
  attemptCount: number
  lastError: string | null
  occurredAt: string
  nextAttemptAt: string | null
  ticketCommentId: string | null
}

// ---- AI assistance (area 7) ----

export enum AiFeature {
  Summary = 0,
  SuggestedReply = 1,
  Categorisation = 2,
  SuggestedSolutions = 3,
  Chatbot = 4,
}

export enum AiCallOutcome {
  Success = 0,
  Failed = 1,
  RateLimited = 2,
  Disabled = 3,
  Cached = 4,
  /** The provider had nothing useful to say. A real answer, not a failure. */
  NoAnswer = 5,
}

export enum AiSuggestionOutcome {
  Offered = 0,
  Accepted = 1,
  Edited = 2,
  Rejected = 3,
}

export interface SuggestedArticle {
  id: string
  titleAr: string
  titleEn: string
  rank: number
}

export interface TicketSummary {
  summary: string
  outcome: AiCallOutcome
  rationale: string | null
  provider: string
}

export interface SuggestedReply {
  body: string
  outcome: AiCallOutcome
  rationale: string | null
  basedOn: SuggestedArticle[]
}

export interface CategorySuggestion {
  categoryId: string | null
  categoryNameAr: string | null
  categoryNameEn: string | null
  outcome: AiCallOutcome
  rationale: string | null
}

export interface SuggestedSolutions {
  articles: SuggestedArticle[]
  outcome: AiCallOutcome
  rationale: string | null
}

export interface ChatAnswer {
  answer: string
  outcome: AiCallOutcome
  rationale: string | null
  sources: SuggestedArticle[]
  shouldEscalate: boolean
}

// ---- Reports (area 8) ----

export enum ReportGranularity {
  Day = 0,
  Week = 1,
  Month = 2,
}

export enum ReportKind {
  Tickets = 0,
  Sla = 1,
  Agents = 2,
  Csat = 3,
}

export enum ExportFormat {
  Xlsx = 0,
  Csv = 1,
}

export interface ReportQuery {
  from: string
  to: string
  granularity?: ReportGranularity
  departmentId?: string | null
  branchId?: string | null
  categoryId?: string | null
  priority?: TicketPriority | null
  status?: TicketStatus | null
  channel?: CommunicationChannel | null
  agentId?: string | null
}

export interface TimeBucket {
  start: string
  count: number
}

export interface DimensionBucket {
  key: string
  labelAr: string
  labelEn: string
  count: number
}

export interface TicketReport {
  total: number
  previousTotal: number
  trend: TimeBucket[]
  previousTrend: TimeBucket[]
  byStatus: DimensionBucket[]
  byPriority: DimensionBucket[]
  byChannel: DimensionBucket[]
  byCategory: DimensionBucket[]
  byDepartment: DimensionBucket[]
}

/** Every ratio is nullable. Null means there was nothing to measure, which the UI renders
 *  as a dash rather than as zero. */
export interface SlaReport {
  measured: number
  firstResponseMet: number
  firstResponseBreached: number
  firstResponseAttainment: number | null
  resolutionMet: number
  resolutionBreached: number
  resolutionAttainment: number | null
  averageFirstResponseMinutes: number | null
  medianFirstResponseMinutes: number | null
  averageResolutionMinutes: number | null
  medianResolutionMinutes: number | null
  breachTrend: TimeBucket[]
  breachesByDepartment: DimensionBucket[]
}

export interface AgentRow {
  agentId: string
  nameAr: string
  nameEn: string
  handled: number
  resolved: number
  reopened: number
  reopenRate: number | null
  averageFirstResponseMinutes: number | null
  averageResolutionMinutes: number | null
  averageSatisfaction: number | null
  currentLoad: number
}

export interface AgentReport {
  rows: AgentRow[]
}

export interface CsatReport {
  averageScore: number | null
  responses: number
  eligibleTickets: number
  responseRate: number | null
  trend: TimeBucket[]
  distribution: DimensionBucket[]
}

// ---- Customer portal (area 9) ----

/** What a customer sees of their own ticket. A separate shape from the staff DTO on purpose:
 *  it has no property for an agent, a department, an escalation level or an SLA date, so
 *  those cannot leak into the portal even by mistake. */
export interface PortalTicket {
  id: string
  number: string
  subject: string
  description: string
  status: TicketStatus
  priority: TicketPriority
  channel: CommunicationChannel
  categoryNameAr: string | null
  categoryNameEn: string | null
  createdAt: string
  resolvedAt: string | null
  closedAt: string | null
  canReply: boolean
  canRate: boolean
  satisfactionScore: number | null
}

export interface PortalTicketListItem {
  id: string
  number: string
  subject: string
  status: TicketStatus
  priority: TicketPriority
  createdAt: string
  lastActivityAt: string | null
}

export interface PortalMessage {
  id: string
  body: string
  fromSupport: boolean
  createdAt: string
}

export interface PortalAttachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  createdAt: string
}

export interface CreatePortalTicketRequest {
  subject: string
  description: string
  priority: TicketPriority
  categoryId: string | null
}

export interface PortalProfile {
  customerId: string
  code: string
  fullNameAr: string
  fullNameEn: string
  email: string | null
  phone: string | null
  preferredLanguage: string
}

// ---- Integrations (area 11) ----

export enum WebhookDeliveryStatus {
  Pending = 0,
  Delivered = 1,
  Retrying = 2,
  Failed = 3,
}

export interface ApiKey {
  id: string
  name: string
  /** The clear part of the key, so an operator can tell two apart without seeing the secret. */
  prefix: string
  scopes: string[]
  isActive: boolean
  expiresAt: string | null
  lastUsedAt: string | null
  rateLimitPerMinute: number
  revokedAt: string | null
  createdAt: string
}

/** The plain key is returned once, at creation, and never again. */
export interface CreatedApiKey {
  key: ApiKey
  plainKey: string
}

export interface SaveApiKeyRequest {
  name: string
  scopes: string[]
  expiresAt: string | null
  rateLimitPerMinute: number
}

export interface WebhookSubscription {
  id: string
  name: string
  url: string
  events: string[]
  isActive: boolean
  consecutiveFailures: number
  lastDeliveryAt: string | null
  disabledAt: string | null
}

export interface CreatedWebhook {
  subscription: WebhookSubscription
  secret: string
}

export interface SaveWebhookRequest {
  name: string
  url: string
  events: string[]
  isActive: boolean
}

export interface WebhookDelivery {
  id: string
  eventType: string
  status: WebhookDeliveryStatus
  attemptCount: number
  lastStatusCode: number | null
  lastError: string | null
  createdAt: string
  deliveredAt: string | null
  nextAttemptAt: string | null
}

export const PERMISSIONS = {
  usersView: 'users.view',
  usersManage: 'users.manage',
  customersView: 'customers.view',
  customersCreate: 'customers.create',
  customersEdit: 'customers.edit',
  customersDelete: 'customers.delete',
  customersMerge: 'customers.merge',
  customersImport: 'customers.import',
  attachmentsView: 'attachments.view',
  attachmentsUpload: 'attachments.upload',
  attachmentsDelete: 'attachments.delete',
  ticketsView: 'tickets.view',
  ticketsCreate: 'tickets.create',
  ticketsEdit: 'tickets.edit',
  ticketsAssign: 'tickets.assign',
  ticketsComment: 'tickets.comment',
  ticketsViewInternal: 'tickets.viewinternal',
  ticketsClose: 'tickets.close',
  ticketsDelete: 'tickets.delete',
  dashboardView: 'dashboard.view',
  dashboardViewTeam: 'dashboard.viewteam',
  kbView: 'kb.view',
  kbManage: 'kb.manage',
  kbPublish: 'kb.publish',
  portalAccess: 'portal.access',
  portalCreateTicket: 'portal.ticket.create',
  portalReply: 'portal.ticket.reply',
  reportsView: 'reports.view',
  reportsExport: 'reports.export',
  slaView: 'sla.view',
  slaManage: 'sla.manage',
  lookupsView: 'lookups.view',
  lookupsManage: 'lookups.manage',
  auditLogsView: 'auditlogs.view',
  systemConfigView: 'systemconfig.view',
  systemConfigManage: 'systemconfig.manage',
} as const

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: CurrentUser
}

export interface Agent {
  id: string
  fullNameAr: string
  fullNameEn: string
  email: string
  departmentId: string | null
  openTicketCount: number
}

// ---- Lookups ----

export interface Lookup {
  id: string
  nameAr: string
  nameEn: string
  code: string | null
}

export interface CategoryLookup {
  id: string
  nameAr: string
  nameEn: string
  parentId: string | null
  departmentId: string | null
  sortOrder: number
  children: CategoryLookup[]
}

// ---- Customers ----

export interface CustomerListItem {
  id: string
  code: string
  fullNameAr: string
  fullNameEn: string
  email: string | null
  phone: string | null
  companyName: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchNameAr: string | null
  branchNameEn: string | null
  isActive: boolean
  openTicketCount: number
  createdAt: string
}

export interface CustomerContact {
  id: string
  type: ContactType
  value: string
  label: string | null
  isPrimary: boolean
}

export interface SaveCustomerContact {
  type: ContactType
  value: string
  label: string | null
  isPrimary: boolean
}

export interface CustomerDetail {
  id: string
  code: string
  fullNameAr: string
  fullNameEn: string
  email: string | null
  phone: string | null
  whatsAppNumber: string | null
  companyName: string | null
  nationalId: string | null
  address: string | null
  preferredLanguage: string
  departmentId: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchId: string | null
  branchNameAr: string | null
  branchNameEn: string | null
  isActive: boolean
  createdAt: string
  modifiedAt: string | null
  contacts: CustomerContact[]
}

export interface SaveCustomerRequest {
  fullNameAr: string
  fullNameEn: string
  email: string | null
  phone: string | null
  whatsAppNumber: string | null
  companyName: string | null
  nationalId: string | null
  address: string | null
  preferredLanguage: string | null
  departmentId: string | null
  branchId: string | null
  isActive?: boolean
  contacts: SaveCustomerContact[] | null
}

export interface CustomerNote {
  id: string
  body: string
  isInternal: boolean
  createdBy: string | null
  createdByName: string | null
  createdAt: string
}

export interface Interaction {
  id: string
  customerId: string
  ticketId: string | null
  ticketNumber: string | null
  channel: CommunicationChannel
  direction: InteractionDirection
  subject: string | null
  body: string
  occurredAt: string
  agentId: string | null
  agentName: string | null
}

// ---- Tickets ----

export interface TicketListItem {
  id: string
  number: string
  subject: string
  status: TicketStatus
  priority: TicketPriority
  channel: CommunicationChannel
  customerId: string
  customerNameAr: string
  customerNameEn: string
  categoryId: string | null
  categoryNameAr: string | null
  categoryNameEn: string | null
  assignedAgentId: string | null
  assignedAgentName: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  escalationLevel: number
  resolutionDueAt: string | null
  createdAt: string
  modifiedAt: string | null
}

export interface TicketDetail {
  id: string
  number: string
  subject: string
  description: string
  status: TicketStatus
  priority: TicketPriority
  channel: CommunicationChannel
  customerId: string
  customerCode: string
  customerNameAr: string
  customerNameEn: string
  customerEmail: string | null
  customerPhone: string | null
  customerPreferredLanguage: string
  categoryId: string | null
  categoryNameAr: string | null
  categoryNameEn: string | null
  assignedAgentId: string | null
  assignedAgentName: string | null
  assignedAt: string | null
  departmentId: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchId: string | null
  branchNameAr: string | null
  branchNameEn: string | null
  escalationLevel: number
  firstResponseDueAt: string | null
  resolutionDueAt: string | null
  firstRespondedAt: string | null
  resolvedAt: string | null
  closedAt: string | null
  createdAt: string
  modifiedAt: string | null
  /** Populated by the server from the domain workflow — drives which status buttons render. */
  allowedNextStatuses: TicketStatus[]
  tags: Tag[]
  watchers: Watcher[]
  links: TicketLink[]
}

export interface CreateTicketRequest {
  customerId: string
  subject: string
  description: string
  priority: TicketPriority
  channel: CommunicationChannel
  categoryId: string | null
  departmentId: string | null
  branchId: string | null
  assignedAgentId: string | null
}

export interface UpdateTicketRequest {
  subject: string
  description: string
  priority: TicketPriority
  categoryId: string | null
  departmentId: string | null
  branchId: string | null
}

export interface TicketComment {
  id: string
  ticketId: string
  body: string
  isInternal: boolean
  authorId: string | null
  authorName: string | null
  createdAt: string
}

export interface TicketHistoryEntry {
  id: string
  field: string
  oldValue: string | null
  newValue: string | null
  note: string | null
  changedBy: string | null
  changedByName: string | null
  changedAt: string
}

export interface AgentDashboard {
  assignedActive: number
  assignedNew: number
  assignedOverdue: number
  unassignedInDepartment: number
  resolvedToday: number
  /** Keyed by the C# enum *name* — System.Text.Json writes enum dictionary keys as names. */
  byStatus: Record<string, number>
  byPriority: Record<string, number>
  recentAssigned: TicketListItem[]
}


// ---- Audit log (area 10) ----

export enum AuditAction {
  Created = 0,
  Updated = 1,
  Deleted = 2,
}

export interface AuditLogEntry {
  id: string
  entityName: string
  entityId: string
  action: AuditAction
  /** Raw JSON: { property: { old, new } }. Null for creates. */
  changes: string | null
  userId: string | null
  userName: string | null
  ipAddress: string | null
  occurredAt: string
}

export interface AuditLogFacets {
  entityNames: string[]
}


// ---- System configuration (area 10) and branding (area 12) ----

export interface Branding {
  companyNameAr: string
  companyNameEn: string
  logoUrl: string | null
  primaryColor: string | null
  secondaryColor: string | null
  defaultLocale: string
}

export interface BusinessHoursDay {
  /** 0 = Sunday, matching System.DayOfWeek. */
  day: number
  isWorkingDay: boolean
  /** "HH:mm:ss", or null on a non-working day. */
  openAt: string | null
  closeAt: string | null
}

export interface Holiday {
  id: string
  date: string
  nameAr: string
  nameEn: string
}

export interface FeatureFlag {
  id: string
  key: string
  isEnabled: boolean
  descriptionAr: string | null
  descriptionEn: string | null
}

export interface ChannelToggle {
  id: string
  channel: CommunicationChannel
  isEnabled: boolean
  endpoint: string | null
  /** Whether a credential is stored. The value itself is never returned by the API. */
  hasCredentials: boolean
}


// ---- Ticket operations (area 2) ----

export enum TicketLinkType {
  DuplicateOf = 0,
  RelatedTo = 1,
  Blocks = 2,
}

export interface Tag {
  id: string
  name: string
  colorHex: string | null
}

export interface Watcher {
  userId: string
  displayName: string
}

export interface TicketLink {
  id: string
  type: TicketLinkType
  /** True when this ticket is the source; the DTO's other* fields are always the far end. */
  isOutgoing: boolean
  otherTicketId: string
  otherTicketNumber: string
  otherTicketSubject: string
  otherTicketStatus: TicketStatus
}

export interface BulkItemResult {
  ticketId: string
  succeeded: boolean
  errorCode: string | null
  errorMessage: string | null
}

export interface BulkOperationResult {
  succeededCount: number
  failedCount: number
  items: BulkItemResult[]
}

export interface SavedView {
  id: string
  name: string
  entityKind: string
  /** Opaque to the server; this client owns the shape. */
  filtersJson: string
}

export interface CategoryUpsertRequest {
  nameAr: string
  nameEn: string
  parentId: string | null
  departmentId: string | null
  sortOrder: number
  isActive: boolean
}

export interface CategoryReorderItem {
  id: string
  parentId: string | null
  sortOrder: number
}


// ---- Customer 360 (area 1) ----

export enum AttachmentOwnerType {
  Customer = 0,
  Ticket = 1,
  TicketComment = 2,
  Interaction = 3,
}

export interface AttachmentDetail {
  id: string
  ownerType: AttachmentOwnerType
  ownerId: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedBy: string | null
  uploadedByName: string | null
  createdAt: string
}

export interface CustomerMergeResult {
  survivorId: string
  mergedCustomerId: string
  ticketsMoved: number
  interactionsMoved: number
  notesMoved: number
  contactsMoved: number
  attachmentsMoved: number
}

export interface CustomerImportRowResult {
  rowNumber: number
  succeeded: boolean
  customerId: string | null
  code: string | null
  errors: string[]
}

export interface CustomerImportResult {
  totalRows: number
  succeededCount: number
  failedCount: number
  rows: CustomerImportRowResult[]
}

export enum CustomerActivityType {
  Ticket = 0,
  Interaction = 1,
  Note = 2,
  Attachment = 3,
}

export interface CustomerActivityItem {
  type: CustomerActivityType
  id: string
  occurredAt: string
  titleAr: string | null
  titleEn: string | null
  snippet: string | null
  refNumber: string | null
  status: string | null
  actorId: string | null
  actorName: string | null
}

/** RFC 7807 problem response, as produced by the API's exception middleware. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
  /** Language-neutral failure identifier. The UI translates this; `detail` is the
   *  server's English text and is only a fallback for codes with no translation. */
  errorCode?: string
  errors?: Record<string, string[]>
}

// ---- User administration (area 10) ----

/** Role names as the API reports them; mirrors Application/Auth/Roles.cs. */
export const ROLE_NAMES = ['Admin', 'Manager', 'Agent', 'Customer'] as const
export type RoleName = (typeof ROLE_NAMES)[number]

export interface UserAdmin {
  id: string
  email: string
  fullNameAr: string
  fullNameEn: string
  preferredLanguage: string
  departmentId: string | null
  departmentNameAr: string | null
  departmentNameEn: string | null
  branchId: string | null
  branchNameAr: string | null
  branchNameEn: string | null
  isActive: boolean
  roles: string[]
  createdAt: string
  lastLoginAt: string | null
}

export interface CreateUserRequest {
  email: string
  password: string
  fullNameAr: string
  fullNameEn: string
  preferredLanguage: string | null
  departmentId: string | null
  branchId: string | null
  roles: string[]
}

export interface UpdateUserRequest {
  fullNameAr: string
  fullNameEn: string
  preferredLanguage: string | null
  departmentId: string | null
  branchId: string | null
  roles: string[]
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}
