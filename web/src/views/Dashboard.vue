<template>
  <div>
    <div class="toolbar">
      <h1>仪表盘</h1>
      <button class="btn" @click="load">刷新</button>
    </div>
    <div v-if="snap" class="panel">
      <div class="toolbar">
        <h3>{{ snap.serverName }}</h3>
        <span class="badge" :class="snap.isOnline ? 'online' : 'offline'">
          {{ snap.isOnline ? '在线' : '离线' }}
        </span>
      </div>
      <template v-if="snap.isOnline && snap.metrics">
        <div class="stat-row"><span>玩家</span><strong>{{ snap.metrics.currentPlayerNum }} / {{ snap.metrics.maxPlayerNum }}</strong></div>
        <div class="stat-row"><span>FPS</span><strong>{{ Number(snap.metrics.serverFps).toFixed(1) }}</strong></div>
        <div class="stat-row"><span>运行时间</span><strong>{{ snap.metrics.uptime || '-' }}</strong></div>
        <div class="stat-row"><span>版本</span><strong>{{ snap.info?.version || '-' }}</strong></div>
      </template>
      <p v-else class="error">{{ snap.error || '无法连接' }}</p>
    </div>
    <p v-else class="empty">暂无状态数据</p>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const snap = ref<any>(null)

async function load() {
  try {
    snap.value = await api.refreshServerMonitor(id())
  } catch (e: any) {
    toast(e.message, 'error')
  }
}

onMounted(load)
</script>
