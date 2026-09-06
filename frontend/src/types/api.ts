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
export const PERMISSIONS = {
  usersView: 'users.view',
  usersManage: 'users.manage',
  customersView: 'customers.view',
  customersCreate: 'customers.create',
  customersEdit: 'customers.edit',
  customersDelete: 'customers.delete',
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

/** RFC 7807 problem response, as produced by the API's exception middleware. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
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
