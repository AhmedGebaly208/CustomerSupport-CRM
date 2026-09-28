import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { ROLES } from '@/stores/auth'
import { PERMISSIONS } from '@/types/api'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { public: true, layout: 'auth' },
  },
  {
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    children: [
      { path: '', redirect: { name: 'dashboard' } },
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('@/views/dashboard/AgentDashboardView.vue'),
      },
      {
        path: 'customers',
        name: 'customers',
        component: () => import('@/views/customers/CustomerListView.vue'),
      },
      {
        path: 'customers/new',
        name: 'customer-new',
        component: () => import('@/views/customers/CustomerFormView.vue'),
      },
      {
        path: 'customers/:id',
        name: 'customer-detail',
        component: () => import('@/views/customers/CustomerDetailView.vue'),
        props: true,
      },
      {
        path: 'customers/:id/edit',
        name: 'customer-edit',
        component: () => import('@/views/customers/CustomerFormView.vue'),
        props: true,
      },
      {
        path: 'account/password',
        name: 'change-password',
        component: () => import('@/views/account/ChangePasswordView.vue'),
      },
      {
        path: 'admin/users',
        name: 'users',
        component: () => import('@/views/admin/users/UserListView.vue'),
        meta: { permission: PERMISSIONS.usersView },
      },
      {
        path: 'admin/users/new',
        name: 'user-new',
        component: () => import('@/views/admin/users/UserFormView.vue'),
        meta: { permission: PERMISSIONS.usersManage },
      },
      {
        path: 'admin/users/:id/edit',
        name: 'user-edit',
        component: () => import('@/views/admin/users/UserFormView.vue'),
        props: true,
        meta: { permission: PERMISSIONS.usersManage },
      },
      {
        path: 'admin/categories',
        name: 'ticket-categories',
        component: () => import('@/views/admin/categories/CategoryAdminView.vue'),
        meta: { permission: PERMISSIONS.lookupsManage },
      },
      {
        path: 'team',
        name: 'team-dashboard',
        component: () => import('@/views/dashboard/TeamDashboardView.vue'),
        meta: { permission: PERMISSIONS.dashboardViewTeam },
      },
      {
        path: 'admin/sla',
        name: 'sla-admin',
        component: () => import('@/views/admin/sla/SlaAdminView.vue'),
        meta: { permission: PERMISSIONS.slaView },
      },
      {
        path: 'admin/settings',
        name: 'system-config',
        component: () => import('@/views/admin/settings/SystemConfigView.vue'),
        meta: { permission: PERMISSIONS.systemConfigView },
      },
      {
        path: 'admin/audit-logs',
        name: 'audit-logs',
        component: () => import('@/views/admin/audit/AuditLogView.vue'),
        meta: { permission: PERMISSIONS.auditLogsView },
      },
      {
        path: 'tickets',
        name: 'tickets',
        component: () => import('@/views/tickets/TicketListView.vue'),
      },
      {
        path: 'tickets/new',
        name: 'ticket-new',
        component: () => import('@/views/tickets/TicketFormView.vue'),
      },
      {
        path: 'tickets/:id',
        name: 'ticket-detail',
        component: () => import('@/views/tickets/TicketDetailView.vue'),
        props: true,
      },
      {
        path: 'tickets/:id/edit',
        name: 'ticket-edit',
        component: () => import('@/views/tickets/TicketFormView.vue'),
        props: true,
      },
    ],
  },
  { path: '/:pathMatch(.*)*', redirect: { name: 'dashboard' } },
]

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // On a hard reload the store is empty even when a valid token exists, so try to
  // rehydrate before deciding the route is unauthorised.
  if (!auth.isAuthenticated) {
    await auth.restore()
  }

  if (to.meta.public) {
    return auth.isAuthenticated && to.name === 'login' ? { name: 'dashboard' } : true
  }

  if (!auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  // Permission guard. A missing permission sends the caller home rather than to an error
  // page: the nav already hides what they cannot reach, so arriving here means a stale
  // bookmark or a hand-typed URL.
  const permission = to.meta.permission as string | undefined
  if (permission && !auth.hasPermission(permission)) {
    return { name: 'dashboard' }
  }

  // Every route in this bootstrap is a staff-facing screen; the customer portal
  // (PDF area 8) will get its own route tree and layout.
  if (!auth.isStaff && !auth.hasRole(ROLES.customer)) {
    return { name: 'login' }
  }

  return true
})
