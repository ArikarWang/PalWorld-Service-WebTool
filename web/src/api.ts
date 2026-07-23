export type ServerSummary = {
  id: string
  name: string
  host: string
  restApiPort: number
  gamePort: number
  isOnline: boolean
  playerCount?: number
  maxPlayers?: number
  checkedAt?: string
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const res = await fetch(`/api${path}`, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options,
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.error || `HTTP ${res.status}`)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

export const api = {
  listServers: () => request<ServerSummary[]>('/servers'),
  getServer: (id: string) => request<any>(`/servers/${id}`),
  login: (id: string, password: string) =>
    request<{ ok: boolean }>(`/servers/${id}/login`, { method: 'POST', body: JSON.stringify({ password }) }),
  logout: (id: string) => request(`/servers/${id}/logout`, { method: 'POST' }),
  getSession: (id: string) => request<{ authenticated: boolean }>(`/servers/${id}/session`),
  monitor: () => request<any[]>('/monitor'),
  refreshMonitor: () => request<any[]>('/monitor/refresh', { method: 'POST' }),
  serverMonitor: (id: string) => request<any>(`/servers/${id}/monitor`),
  refreshServerMonitor: (id: string) => request<any>(`/servers/${id}/monitor/refresh`, { method: 'POST' }),
  players: (id: string) => request<any[]>(`/servers/${id}/players`),
  playerPals: (id: string, playerKey: string) =>
    request<any>(`/servers/${id}/players/${encodeURIComponent(playerKey)}/pals`),
  health: () => request<{ status: string }>('/health'),
  announce: (id: string, message: string) =>
    request(`/servers/${id}/announce`, { method: 'POST', body: JSON.stringify({ message }) }),
  save: (id: string) => request(`/servers/${id}/save`, { method: 'POST' }),
  shutdown: (id: string, waitTime = 60, message?: string) =>
    request(`/servers/${id}/shutdown`, { method: 'POST', body: JSON.stringify({ waitTime, message }) }),
  stop: (id: string) => request(`/servers/${id}/stop`, { method: 'POST' }),
  kick: (id: string, userId: string, message?: string) =>
    request(`/servers/${id}/kick`, { method: 'POST', body: JSON.stringify({ userId, message }) }),
  ban: (id: string, userId: string, message?: string) =>
    request(`/servers/${id}/ban`, { method: 'POST', body: JSON.stringify({ userId, message }) }),
  getConfig: (id: string) => request<{ content: string }>(`/servers/${id}/config`),
  putConfig: (id: string, content: string) =>
    request(`/servers/${id}/config`, { method: 'PUT', body: JSON.stringify({ content }) }),
  logs: (id: string, lines = 300) => request<string[]>(`/servers/${id}/logs?lines=${lines}`),
  process: (id: string) => request<{ running: boolean }>(`/servers/${id}/process`),
  processStart: (id: string) => request(`/servers/${id}/process/start`, { method: 'POST' }),
  processStop: (id: string) => request(`/servers/${id}/process/stop`, { method: 'POST' }),
  backups: (id: string) => request<any[]>(`/servers/${id}/backups`),
  createBackup: (id: string) => request(`/servers/${id}/backups`, { method: 'POST' }),
  restoreBackup: (id: string, fileName: string) =>
    request(`/servers/${id}/backups/${encodeURIComponent(fileName)}/restore`, { method: 'POST' }),
  schedules: (id: string) => request<any[]>(`/servers/${id}/schedules`),
  addSchedule: (id: string, body: any) =>
    request(`/servers/${id}/schedules`, { method: 'POST', body: JSON.stringify(body) }),
  deleteSchedule: (id: string, taskId: string) =>
    request(`/servers/${id}/schedules/${taskId}`, { method: 'DELETE' }),
  shutdownService: () => request('/system/shutdown', { method: 'POST' }),
}

export function rememberedPasswordKey(serverId: string) {
  return `pal.webPassword.${serverId}`
}
