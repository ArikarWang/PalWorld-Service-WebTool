<template>
  <div>
    <div class="toolbar">
      <h1>玩家</h1>
      <button class="btn" @click="load">刷新</button>
    </div>
    <p class="hint">在线来自 REST API；离线来自存档目录 Players/*.sav 合并。点击行查看帕鲁（能力评估中）。</p>
    <table class="table">
      <thead>
        <tr>
          <th>状态</th><th>名称</th><th>ID</th><th>IP</th><th>Ping</th><th>等级</th><th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="p in players"
          :key="p.key"
          class="click-row"
          @click="openPals(p)"
        >
          <td>
            <span class="badge" :class="p.isOnline ? 'online' : 'offline'">
              {{ p.isOnline ? '在线' : '离线' }}
            </span>
          </td>
          <td>{{ p.name || '(存档玩家)' }}</td>
          <td><code>{{ p.userId || p.playerUid || p.key }}</code></td>
          <td>{{ p.ip || '-' }}</td>
          <td>{{ p.isOnline ? p.ping : '-' }}</td>
          <td>{{ p.level || '-' }}</td>
          <td @click.stop>
            <button class="btn sm" @click="openPals(p)">帕鲁</button>
            <button
              v-if="p.isOnline && p.userId"
              class="btn sm danger"
              @click="kick(p.userId)"
            >踢出</button>
            <button
              v-if="p.isOnline && p.userId"
              class="btn sm danger"
              @click="ban(p.userId)"
            >封禁</button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="!players.length" class="empty">暂无玩家（无在线且未扫描到存档）</p>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const router = useRouter()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const players = ref<any[]>([])

async function load() {
  try { players.value = await api.players(id()) }
  catch (e: any) { toast(e.message, 'error') }
}

function openPals(p: any) {
  router.push({
    name: 'player-pals',
    params: { id: id(), playerKey: p.key },
    query: { name: p.name || '', userId: p.userId || p.playerUid || '' }
  })
}

async function kick(userId: string) {
  const message = prompt('踢出原因（可选）') || ''
  try {
    await api.kick(id(), userId, message)
    toast('已踢出')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

async function ban(userId: string) {
  const message = prompt('封禁原因（可选）') || ''
  try {
    await api.ban(id(), userId, message)
    toast('已封禁')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

onMounted(load)
</script>

<style scoped>
.click-row { cursor: pointer; }
.click-row:hover td { background: rgba(59, 130, 246, 0.08); }
</style>
