import { http } from './client'
import type {
  AgentReport,
  AiFeature,
  AiSuggestionOutcome,
  CsatReport,
  ExportFormat,
  ReportKind,
  ReportQuery,
  SlaReport,
  TicketReport,
} from '@/types/api'
import type {
  Agent,
  AgentDashboard,
  AgentTask,
  AgentWorkspace,
  ArticleCategory,
  ArticleDetail,
  ArticleSearchResult,
  ArticleStatus,
  ArticleTicketLink,
  ArticleVersion,
  ArticleVoteResult,
  AttachmentDetail,
  AuditAction,
  AuditLogEntry,
  AuditLogFacets,
  AuthResponse,
  Branding,
  BulkOperationResult,
  BusinessHoursDay,
  CategoryLookup,
  CategoryReorderItem,
  CategorySuggestion,
  CategoryUpsertRequest,
  ChangePasswordRequest,
  ChannelMessage,
  ChannelStatus,
  ChannelToggle,
  ChatAnswer,
  CommunicationChannel,
  CreateTicketRequest,
  CreateUserRequest,
  CurrentUser,
  CustomerActivityItem,
  CustomerActivityType,
  CustomerDetail,
  CustomerImportResult,
  CustomerListItem,
  CustomerMergeResult,
  CustomerNote,
  FeatureFlag,
  Holiday,
  Interaction,
  InteractionDirection,
  Lookup,
  NotificationList,
  PagedResult,
  QuickReply,
  SaveAgentTaskRequest,
  SaveArticleCategoryRequest,
  SaveArticleRequest,
  SaveCustomerRequest,
  SaveQuickReplyRequest,
  SaveSlaEscalationRuleRequest,
  SaveSlaPolicyRequest,
  SavedView,
  SlaEscalationRule,
  SlaPolicy,
  SlaPreview,
  SlaPreviewRequest,
  SuggestedReply,
  SuggestedSolutions,
  Tag,
  TeamDashboard,
  TicketComment,
  TicketDetail,
  TicketHistoryEntry,
  TicketLink,
  TicketLinkType,
  TicketListItem,
  TicketPriority,
  TicketSlaStatus,
  TicketStatus,
  TicketSummary,
  UpdateTicketRequest,
  UpdateUserRequest,
  UserAdmin,
  Watcher,
} from '@/types/api'

export const authApi = {
  async login(email: string, password: string) {
    const { data } = await http.post<AuthResponse>('/auth/login', { email, password })
    return data
  },
  async me() {
    const { data } = await http.get<CurrentUser>('/auth/me')
    return data
  },
  async agents(departmentId?: string | null) {
    const { data } = await http.get<Agent[]>('/agents', { params: { departmentId } })
    return data
  },
  async changePassword(request: ChangePasswordRequest) {
    await http.post('/auth/change-password', request)
  },
}

export interface UserQueryParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDescending?: boolean
  departmentId?: string | null
  branchId?: string | null
  role?: string | null
  isActive?: boolean | null
}

export const usersApi = {
  async list(params: UserQueryParams) {
    const { data } = await http.get<PagedResult<UserAdmin>>('/users', { params })
    return data
  },
  async get(id: string) {
    const { data } = await http.get<UserAdmin>(`/users/${id}`)
    return data
  },
  async create(request: CreateUserRequest) {
    const { data } = await http.post<UserAdmin>('/users', request)
    return data
  },
  async update(id: string, request: UpdateUserRequest) {
    const { data } = await http.put<UserAdmin>(`/users/${id}`, request)
    return data
  },
  async deactivate(id: string) {
    await http.post(`/users/${id}/deactivate`)
  },
  async reactivate(id: string) {
    await http.post(`/users/${id}/reactivate`)
  },
  async setRoles(id: string, roles: string[]) {
    await http.put(`/users/${id}/roles`, { roles })
  },
  async availableRoles() {
    const { data } = await http.get<string[]>('/roles')
    return data
  },
}

export interface CustomerQueryParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDescending?: boolean
  departmentId?: string | null
  branchId?: string | null
  isActive?: boolean | null
}

export const customersApi = {
  async search(params: CustomerQueryParams) {
    const { data } = await http.get<PagedResult<CustomerListItem>>('/customers', { params })
    return data
  },
  async getById(id: string) {
    const { data } = await http.get<CustomerDetail>(`/customers/${id}`)
    return data
  },
  async create(request: SaveCustomerRequest) {
    const { data } = await http.post<CustomerDetail>('/customers', request)
    return data
  },
  async update(id: string, request: SaveCustomerRequest) {
    const { data } = await http.put<CustomerDetail>(`/customers/${id}`, request)
    return data
  },
  async remove(id: string) {
    await http.delete(`/customers/${id}`)
  },
  async notes(id: string) {
    const { data } = await http.get<CustomerNote[]>(`/customers/${id}/notes`)
    return data
  },
  async addNote(id: string, body: string, isInternal: boolean) {
    const { data } = await http.post<CustomerNote>(`/customers/${id}/notes`, { body, isInternal })
    return data
  },
  async deleteNote(id: string, noteId: string) {
    await http.delete(`/customers/${id}/notes/${noteId}`)
  },
  async interactions(id: string, page = 1, pageSize = 20) {
    const { data } = await http.get<PagedResult<Interaction>>(`/customers/${id}/interactions`, {
      params: { page, pageSize },
    })
    return data
  },
  async addInteraction(
    id: string,
    request: {
      channel: CommunicationChannel
      direction: InteractionDirection
      subject: string | null
      body: string
      occurredAt: string | null
      ticketId: string | null
    },
  ) {
    const { data } = await http.post<Interaction>(`/customers/${id}/interactions`, request)
    return data
  },

  // ---- Attachments ----

  async attachments(id: string) {
    const { data } = await http.get<AttachmentDetail[]>(`/customers/${id}/attachments`)
    return data
  },
  async uploadAttachment(id: string, file: File) {
    const form = new FormData()
    form.append('file', file)
    // Let the browser set the multipart boundary; overriding Content-Type breaks it.
    const { data } = await http.post<AttachmentDetail>(`/customers/${id}/attachments`, form, {
      headers: { 'Content-Type': undefined },
    })
    return data
  },
  /** Absolute download URL; the browser fetches it with the session's bearer token. */
  attachmentUrl(id: string, attachmentId: string) {
    return `${http.defaults.baseURL}/customers/${id}/attachments/${attachmentId}`
  },
  async downloadAttachment(id: string, attachmentId: string) {
    const { data } = await http.get<Blob>(`/customers/${id}/attachments/${attachmentId}`, {
      responseType: 'blob',
    })
    return data
  },
  async deleteAttachment(id: string, attachmentId: string) {
    await http.delete(`/customers/${id}/attachments/${attachmentId}`)
  },

  // ---- Merge ----

  async merge(survivorId: string, loserId: string, reason?: string | null) {
    const { data } = await http.post<CustomerMergeResult>('/customers/merge', {
      survivorId,
      loserId,
      reason: reason ?? null,
    })
    return data
  },

  // ---- Import ----

  async import(file: File) {
    const form = new FormData()
    form.append('file', file)
    const { data } = await http.post<CustomerImportResult>('/customers/import', form, {
      headers: { 'Content-Type': undefined },
    })
    return data
  },

  // ---- Activity timeline ----

  async activity(id: string, page = 1, pageSize = 25, types?: CustomerActivityType[]) {
    const { data } = await http.get<PagedResult<CustomerActivityItem>>(`/customers/${id}/activity`, {
      params: { page, pageSize, types },
      paramsSerializer: { indexes: null },
    })
    return data
  },
}

export interface TicketQueryParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDescending?: boolean
  statuses?: TicketStatus[]
  priorities?: TicketPriority[]
  channels?: CommunicationChannel[]
  customerId?: string | null
  categoryId?: string | null
  assignedAgentId?: string | null
  departmentId?: string | null
  branchId?: string | null
  onlyActive?: boolean | null
  unassigned?: boolean | null
  tagIds?: string[]
  watchedBy?: string | null
}

export const ticketsApi = {
  async search(params: TicketQueryParams) {
    const { data } = await http.get<PagedResult<TicketListItem>>('/tickets', {
      params,
      // ASP.NET binds repeated keys (statuses=0&statuses=1), not the bracketed form
      // axios produces by default.
      paramsSerializer: { indexes: null },
    })
    return data
  },
  async getById(id: string) {
    const { data } = await http.get<TicketDetail>(`/tickets/${id}`)
    return data
  },
  async create(request: CreateTicketRequest) {
    const { data } = await http.post<TicketDetail>('/tickets', request)
    return data
  },
  async update(id: string, request: UpdateTicketRequest) {
    const { data } = await http.put<TicketDetail>(`/tickets/${id}`, request)
    return data
  },
  async remove(id: string) {
    await http.delete(`/tickets/${id}`)
  },
  async assign(id: string, agentId: string | null, note?: string | null) {
    const { data } = await http.post<TicketDetail>(`/tickets/${id}/assign`, { agentId, note: note ?? null })
    return data
  },
  async changeStatus(id: string, status: TicketStatus, note?: string | null) {
    const { data } = await http.post<TicketDetail>(`/tickets/${id}/status`, { status, note: note ?? null })
    return data
  },
  async comments(id: string) {
    const { data } = await http.get<TicketComment[]>(`/tickets/${id}/comments`)
    return data
  },
  async addComment(id: string, body: string, isInternal: boolean) {
    const { data } = await http.post<TicketComment>(`/tickets/${id}/comments`, { body, isInternal })
    return data
  },
  async history(id: string) {
    const { data } = await http.get<TicketHistoryEntry[]>(`/tickets/${id}/history`)
    return data
  },
  async agentDashboard(agentId?: string | null) {
    const { data } = await http.get<AgentDashboard>('/dashboard/agent', { params: { agentId } })
    return data
  },

  // ---- Bulk operations ----
  // Each resolves with a per-item result; a failed item does not fail the batch.

  async bulkAssign(ticketIds: string[], agentId: string | null, note?: string | null) {
    const { data } = await http.post<BulkOperationResult>('/tickets/bulk/assign', {
      ticketIds,
      agentId,
      note: note ?? null,
    })
    return data
  },
  async bulkPriority(ticketIds: string[], priority: TicketPriority) {
    const { data } = await http.post<BulkOperationResult>('/tickets/bulk/priority', { ticketIds, priority })
    return data
  },
  async bulkStatus(ticketIds: string[], status: TicketStatus, note?: string | null) {
    const { data } = await http.post<BulkOperationResult>('/tickets/bulk/status', {
      ticketIds,
      status,
      note: note ?? null,
    })
    return data
  },

  // ---- Links ----

  async links(id: string) {
    const { data } = await http.get<TicketLink[]>(`/tickets/${id}/links`)
    return data
  },
  async addLink(id: string, targetTicketId: string, type: TicketLinkType) {
    const { data } = await http.post<TicketLink[]>(`/tickets/${id}/links`, { targetTicketId, type })
    return data
  },
  async removeLink(id: string, linkId: string) {
    const { data } = await http.delete<TicketLink[]>(`/tickets/${id}/links/${linkId}`)
    return data
  },

  // ---- Merge ----

  /** Returns the target ticket; this ticket is closed and its content moved across. */
  async merge(id: string, targetTicketId: string, reason?: string | null) {
    const { data } = await http.post<TicketDetail>(`/tickets/${id}/merge`, {
      targetTicketId,
      reason: reason ?? null,
    })
    return data
  },

  // ---- Watchers ----

  async watchers(id: string) {
    const { data } = await http.get<Watcher[]>(`/tickets/${id}/watchers`)
    return data
  },
  async addWatcher(id: string, userId: string) {
    const { data } = await http.post<Watcher[]>(`/tickets/${id}/watchers`, { userId })
    return data
  },
  async removeWatcher(id: string, userId: string) {
    const { data } = await http.delete<Watcher[]>(`/tickets/${id}/watchers/${userId}`)
    return data
  },

  // ---- Tags ----

  async tags(id: string) {
    const { data } = await http.get<Tag[]>(`/tickets/${id}/tags`)
    return data
  },
  async addTag(id: string, name: string, colorHex?: string | null) {
    const { data } = await http.post<Tag[]>(`/tickets/${id}/tags`, { name, colorHex: colorHex ?? null })
    return data
  },
  async removeTag(id: string, tagId: string) {
    const { data } = await http.delete<Tag[]>(`/tickets/${id}/tags/${tagId}`)
    return data
  },
  async searchTags(query?: string) {
    const { data } = await http.get<Tag[]>('/tags', { params: { query } })
    return data
  },

  // ---- Escalation ----

  /** delta is +1 or -1; the reason is mandatory. */
  async changeEscalation(id: string, delta: number, reason: string) {
    const { data } = await http.post<TicketDetail>(`/tickets/${id}/escalation`, { delta, reason })
    return data
  },
}

export const ticketCategoriesApi = {
  async tree(includeInactive = true) {
    const { data } = await http.get<CategoryLookup[]>('/ticket-categories', { params: { includeInactive } })
    return data
  },
  async create(request: CategoryUpsertRequest) {
    const { data } = await http.post<CategoryLookup>('/ticket-categories', request)
    return data
  },
  async update(id: string, request: CategoryUpsertRequest) {
    const { data } = await http.put<CategoryLookup>(`/ticket-categories/${id}`, request)
    return data
  },
  async reorder(items: CategoryReorderItem[]) {
    await http.post('/ticket-categories/reorder', { items })
  },
  async setActive(id: string, isActive: boolean) {
    await http.post(`/ticket-categories/${id}/${isActive ? 'activate' : 'deactivate'}`)
  },
  async remove(id: string) {
    await http.delete(`/ticket-categories/${id}`)
  },
}

export const savedViewsApi = {
  async list(entityKind = 'Ticket') {
    const { data } = await http.get<SavedView[]>('/saved-views', { params: { entityKind } })
    return data
  },
  async upsert(name: string, filtersJson: string, entityKind = 'Ticket') {
    const { data } = await http.post<SavedView>('/saved-views', { name, entityKind, filtersJson })
    return data
  },
  async remove(id: string) {
    await http.delete(`/saved-views/${id}`)
  },
}

export interface AuditLogQueryParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDescending?: boolean
  entityName?: string | null
  entityId?: string | null
  action?: AuditAction | null
  userId?: string | null
  dateFrom?: string | null
  dateTo?: string | null
}

export const auditLogsApi = {
  async list(params: AuditLogQueryParams) {
    const { data } = await http.get<PagedResult<AuditLogEntry>>('/audit-logs', { params })
    return data
  },
  async get(id: string) {
    const { data } = await http.get<AuditLogEntry>(`/audit-logs/${id}`)
    return data
  },
  async facets() {
    const { data } = await http.get<AuditLogFacets>('/audit-logs/facets')
    return data
  },
}

export const systemConfigApi = {
  /** Anonymous: the login screen renders before anyone has a token. */
  async branding() {
    const { data } = await http.get<Branding>('/branding')
    return data
  },
  async updateBranding(request: Omit<Branding, never>) {
    const { data } = await http.put<Branding>('/branding', request)
    return data
  },
  async businessHours() {
    const { data } = await http.get<BusinessHoursDay[]>('/system-config/business-hours')
    return data
  },
  async saveBusinessHours(days: BusinessHoursDay[]) {
    const { data } = await http.put<BusinessHoursDay[]>('/system-config/business-hours', { days })
    return data
  },
  async holidays(year?: number) {
    const { data } = await http.get<Holiday[]>('/system-config/holidays', { params: { year } })
    return data
  },
  async addHoliday(request: { date: string; nameAr: string; nameEn: string }) {
    const { data } = await http.post<Holiday>('/system-config/holidays', request)
    return data
  },
  async deleteHoliday(id: string) {
    await http.delete(`/system-config/holidays/${id}`)
  },
  async featureFlags() {
    const { data } = await http.get<FeatureFlag[]>('/system-config/feature-flags')
    return data
  },
  async saveFeatureFlag(request: {
    key: string
    isEnabled: boolean
    descriptionAr: string | null
    descriptionEn: string | null
  }) {
    const { data } = await http.put<FeatureFlag>('/system-config/feature-flags', request)
    return data
  },
  async channels() {
    const { data } = await http.get<ChannelToggle[]>('/system-config/channels')
    return data
  },
  /** Omit a credential to leave it unchanged; send "" to clear it. */
  async updateChannel(
    channel: CommunicationChannel,
    request: { isEnabled: boolean; endpoint: string | null; apiKey?: string; apiSecret?: string },
  ) {
    const { data } = await http.put<ChannelToggle>(`/system-config/channels/${channel}`, request)
    return data
  },
}

export const lookupsApi = {
  async departments() {
    const { data } = await http.get<Lookup[]>('/lookups/departments')
    return data
  },
  async branches() {
    const { data } = await http.get<Lookup[]>('/lookups/branches')
    return data
  },
  async categories(departmentId?: string | null) {
    const { data } = await http.get<CategoryLookup[]>('/lookups/categories', { params: { departmentId } })
    return data
  },
}

export const slaApi = {
  async policies() {
    const { data } = await http.get<SlaPolicy[]>('/sla/policies')
    return data
  },
  async policy(id: string) {
    const { data } = await http.get<SlaPolicy>(`/sla/policies/${id}`)
    return data
  },
  async createPolicy(request: SaveSlaPolicyRequest) {
    const { data } = await http.post<SlaPolicy>('/sla/policies', request)
    return data
  },
  async updatePolicy(id: string, request: SaveSlaPolicyRequest) {
    const { data } = await http.put<SlaPolicy>(`/sla/policies/${id}`, request)
    return data
  },
  async deletePolicy(id: string) {
    await http.delete(`/sla/policies/${id}`)
  },

  async addRule(policyId: string, request: SaveSlaEscalationRuleRequest) {
    const { data } = await http.post<SlaEscalationRule>(`/sla/policies/${policyId}/rules`, request)
    return data
  },
  async updateRule(policyId: string, ruleId: string, request: SaveSlaEscalationRuleRequest) {
    const { data } = await http.put<SlaEscalationRule>(`/sla/policies/${policyId}/rules/${ruleId}`, request)
    return data
  },
  async deleteRule(policyId: string, ruleId: string) {
    await http.delete(`/sla/policies/${policyId}/rules/${ruleId}`)
  },

  /** Shows what a policy would promise before committing to it — working-hours arithmetic
   *  is hard to predict by eye. */
  async preview(request: SlaPreviewRequest) {
    const { data } = await http.post<SlaPreview>('/sla/policies/preview', request)
    return data
  },

  async ticketStatus(ticketId: string) {
    const { data } = await http.get<TicketSlaStatus>(`/sla/tickets/${ticketId}`)
    return data
  },
  /** One call for a whole page of tickets, so a list does not fire a request per row. */
  async ticketStatuses(ticketIds: string[]) {
    const { data } = await http.post<Record<string, TicketSlaStatus>>('/sla/tickets', ticketIds)
    return data
  },
  async evaluate() {
    const { data } = await http.post<{ escalated: number }>('/sla/evaluate')
    return data
  },
}

export const notificationsApi = {
  async list(unreadOnly = false, take = 20) {
    const { data } = await http.get<NotificationList>('/notifications', { params: { unreadOnly, take } })
    return data
  },
  async markRead(id: string) {
    await http.post(`/notifications/${id}/read`)
  },
  async markAllRead() {
    await http.post('/notifications/read-all')
  },
}

export const workspaceApi = {
  /** The signed-in agent's board. No id parameter — it is always the caller's own. */
  async mine() {
    const { data } = await http.get<AgentWorkspace>('/workspace/me')
    return data
  },
  async team(departmentId?: string | null) {
    const { data } = await http.get<TeamDashboard>('/workspace/team', { params: { departmentId } })
    return data
  },

  async tasks(params: { includeDone?: boolean; dueBefore?: string; take?: number } = {}) {
    const { data } = await http.get<AgentTask[]>('/workspace/tasks', { params })
    return data
  },
  async createTask(request: SaveAgentTaskRequest) {
    const { data } = await http.post<AgentTask>('/workspace/tasks', request)
    return data
  },
  async updateTask(id: string, request: SaveAgentTaskRequest) {
    const { data } = await http.put<AgentTask>(`/workspace/tasks/${id}`, request)
    return data
  },
  async setTaskDone(id: string, done: boolean) {
    const { data } = await http.post<AgentTask>(`/workspace/tasks/${id}/done`, null, { params: { done } })
    return data
  },
  async deleteTask(id: string) {
    await http.delete(`/workspace/tasks/${id}`)
  },

  async quickReplies() {
    const { data } = await http.get<QuickReply[]>('/workspace/quick-replies')
    return data
  },
  async createQuickReply(request: SaveQuickReplyRequest) {
    const { data } = await http.post<QuickReply>('/workspace/quick-replies', request)
    return data
  },
  async updateQuickReply(id: string, request: SaveQuickReplyRequest) {
    const { data } = await http.put<QuickReply>(`/workspace/quick-replies/${id}`, request)
    return data
  },
  async deleteQuickReply(id: string) {
    await http.delete(`/workspace/quick-replies/${id}`)
  },
}

export const kbApi = {
  async search(params: {
    search?: string
    categoryId?: string | null
    tag?: string | null
    status?: ArticleStatus | null
    isFaq?: boolean | null
    page?: number
    pageSize?: number
  } = {}) {
    const { data } = await http.get<ArticleSearchResult>('/kb/articles', { params })
    return data
  },
  async article(id: string) {
    const { data } = await http.get<ArticleDetail>(`/kb/articles/${id}`)
    return data
  },
  /** Resolves the stable identifier rather than the row id, so a shared link survives. */
  async bySlug(slug: string) {
    const { data } = await http.get<ArticleDetail>(`/kb/articles/by-slug/${encodeURIComponent(slug)}`)
    return data
  },
  async create(request: SaveArticleRequest) {
    const { data } = await http.post<ArticleDetail>('/kb/articles', request)
    return data
  },
  async update(id: string, request: SaveArticleRequest) {
    const { data } = await http.put<ArticleDetail>(`/kb/articles/${id}`, request)
    return data
  },
  async setStatus(id: string, status: ArticleStatus) {
    const { data } = await http.post<ArticleDetail>(`/kb/articles/${id}/status`, null, { params: { status } })
    return data
  },
  async remove(id: string) {
    await http.delete(`/kb/articles/${id}`)
  },
  async versions(id: string) {
    const { data } = await http.get<ArticleVersion[]>(`/kb/articles/${id}/versions`)
    return data
  },
  async restoreVersion(id: string, versionId: string) {
    const { data } = await http.post<ArticleDetail>(`/kb/articles/${id}/versions/${versionId}/restore`)
    return data
  },
  async recordView(id: string) {
    await http.post(`/kb/articles/${id}/view`)
  },
  async vote(id: string, isHelpful: boolean) {
    const { data } = await http.post<ArticleVoteResult>(`/kb/articles/${id}/vote`, { isHelpful })
    return data
  },

  async categories(activeOnly = false) {
    const { data } = await http.get<ArticleCategory[]>('/kb/categories', { params: { activeOnly } })
    return data
  },
  async createCategory(request: SaveArticleCategoryRequest) {
    const { data } = await http.post<ArticleCategory>('/kb/categories', request)
    return data
  },
  async updateCategory(id: string, request: SaveArticleCategoryRequest) {
    const { data } = await http.put<ArticleCategory>(`/kb/categories/${id}`, request)
    return data
  },
  async deleteCategory(id: string) {
    await http.delete(`/kb/categories/${id}`)
  },

  async ticketArticles(ticketId: string) {
    const { data } = await http.get<ArticleTicketLink[]>(`/tickets/${ticketId}/articles`)
    return data
  },
  async linkToTicket(ticketId: string, articleId: string) {
    const { data } = await http.post<ArticleTicketLink>(`/tickets/${ticketId}/articles`, { articleId })
    return data
  },
  async unlinkFromTicket(ticketId: string, linkId: string) {
    await http.delete(`/tickets/${ticketId}/articles/${linkId}`)
  },
}

export const channelsApi = {
  async status() {
    const { data } = await http.get<ChannelStatus[]>('/channels')
    return data
  },
  /** The delivery ledger for one ticket — did the reply actually leave? */
  async messages(ticketId: string) {
    const { data } = await http.get<ChannelMessage[]>('/channels/messages', { params: { ticketId } })
    return data
  },
  async dispatch() {
    const { data } = await http.post<{ delivered: number }>('/channels/dispatch')
    return data
  },
}

export const aiApi = {
  async summary(ticketId: string) {
    const { data } = await http.post<TicketSummary>(`/ai/tickets/${ticketId}/summary`)
    return data
  },
  async suggestReply(ticketId: string) {
    const { data } = await http.post<SuggestedReply>(`/ai/tickets/${ticketId}/reply`)
    return data
  },
  async suggestCategory(ticketId: string) {
    const { data } = await http.post<CategorySuggestion>(`/ai/tickets/${ticketId}/category`)
    return data
  },
  async suggestSolutions(ticketId: string) {
    const { data } = await http.post<SuggestedSolutions>(`/ai/tickets/${ticketId}/solutions`)
    return data
  },
  async ask(question: string, languageHint?: string) {
    const { data } = await http.post<ChatAnswer>('/ai/ask', { question, languageHint })
    return data
  },
  /** Records what the agent did with a suggestion, which is how its value is measured. */
  async feedback(feature: AiFeature, outcome: AiSuggestionOutcome, ticketId?: string) {
    await http.post('/ai/feedback', { feature, outcome, ticketId: ticketId ?? null })
  },
}

export const reportsApi = {
  async tickets(query: ReportQuery) {
    const { data } = await http.post<TicketReport>('/reports/tickets', query)
    return data
  },
  async sla(query: ReportQuery) {
    const { data } = await http.post<SlaReport>('/reports/sla', query)
    return data
  },
  async agents(query: ReportQuery) {
    const { data } = await http.post<AgentReport>('/reports/agents', query)
    return data
  },
  async csat(query: ReportQuery) {
    const { data } = await http.post<CsatReport>('/reports/csat', query)
    return data
  },
  /** Returns the file itself; the caller saves it. */
  async exportReport(kind: ReportKind, format: ExportFormat, query: ReportQuery, language: string) {
    const { data } = await http.post(
      '/reports/export',
      { kind, format, query },
      { params: { language }, responseType: 'blob' },
    )
    return data as Blob
  },
  async rateTicket(ticketId: string, score: number, comment?: string) {
    const { data } = await http.post(`/tickets/${ticketId}/satisfaction`, {
      score,
      comment: comment ?? null,
    })
    return data
  },
}
