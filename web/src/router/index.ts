import { createRouter, createWebHistory } from 'vue-router'
import ServerList from '../views/ServerList.vue'
import ServerLogin from '../views/ServerLogin.vue'
import ServerLayout from '../views/ServerLayout.vue'
import Dashboard from '../views/Dashboard.vue'
import Players from '../views/Players.vue'
import Control from '../views/Control.vue'
import ConfigView from '../views/ConfigView.vue'
import LogsView from '../views/LogsView.vue'
import BackupView from '../views/BackupView.vue'
import ScheduleView from '../views/ScheduleView.vue'
import PlayerPals from '../views/PlayerPals.vue'
import Offline from '../views/Offline.vue'
import { api } from '../api'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: ServerList },
    { path: '/offline', name: 'offline', component: Offline },
    { path: '/servers/:id/login', name: 'login', component: ServerLogin, props: true },
    {
      path: '/servers/:id',
      component: ServerLayout,
      props: true,
      children: [
        { path: '', redirect: { name: 'dashboard' } },
        { path: 'dashboard', name: 'dashboard', component: Dashboard },
        { path: 'players', name: 'players', component: Players },
        { path: 'players/:playerKey/pals', name: 'player-pals', component: PlayerPals },
        { path: 'control', name: 'control', component: Control },
        { path: 'config', name: 'config', component: ConfigView },
        { path: 'logs', name: 'logs', component: LogsView },
        { path: 'backup', name: 'backup', component: BackupView },
        { path: 'schedules', name: 'schedules', component: ScheduleView },
      ]
    }
  ]
})

router.beforeEach(async (to) => {
  if (to.name === 'offline') return true

  // If API is down, force offline page (except when already going there)
  try {
    await api.health()
  } catch {
    return { name: 'offline' }
  }

  const id = to.params.id as string | undefined
  if (!id || to.name === 'login') return true
  if (!to.path.startsWith('/servers/')) return true
  try {
    const s = await api.getSession(id)
    if (!s.authenticated) {
      return { name: 'login', params: { id }, query: { redirect: to.fullPath } }
    }
  } catch {
    return { name: 'offline' }
  }
  return true
})

export default router
