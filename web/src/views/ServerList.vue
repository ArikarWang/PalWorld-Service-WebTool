<template>
  <div class="page">
    <div class="toolbar">
      <h1>服务器列表</h1>
      <button class="btn" @click="load" :disabled="loading">刷新</button>
    </div>
    <p class="hint">点击服务器进入。密码在 config/servers.yaml 的 webPassword 中配置。</p>
    <div v-if="error" class="error">{{ error }}</div>
    <div class="card-grid">
      <div v-for="s in servers" :key="s.id" class="card" @click="open(s.id)">
        <div class="toolbar" style="margin:0">
          <h3>{{ s.name }}</h3>
          <span class="badge" :class="s.isOnline ? 'online' : 'offline'">
            {{ s.isOnline ? '在线' : '离线' }}
          </span>
        </div>
        <div class="meta">
          <div>{{ s.host }}:{{ s.restApiPort }}</div>
          <div v-if="s.playerCount != null">玩家 {{ s.playerCount }} / {{ s.maxPlayers ?? '-' }}</div>
        </div>
      </div>
    </div>
    <p v-if="!servers.length && !loading" class="empty">配置文件中暂无服务器。</p>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, rememberedPasswordKey, type ServerSummary } from '../api'

const router = useRouter()
const servers = ref<ServerSummary[]>([])
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    servers.value = await api.listServers()
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function open(id: string) {
  const remembered = localStorage.getItem(rememberedPasswordKey(id))
  if (remembered) {
    try {
      await api.login(id, remembered)
      router.push(`/servers/${id}/dashboard`)
      return
    } catch {
      localStorage.removeItem(rememberedPasswordKey(id))
    }
  }
  router.push(`/servers/${id}/login`)
}

onMounted(load)
</script>
