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

    <div class="panel tool-update">
      <h3>管理工具更新</h3>
      <p class="meta" style="margin-bottom: var(--space-3)">
        当前版本 <strong class="mono">{{ toolVersion || '…' }}</strong>。对比 GitHub Release，仅检查不会自动升级。
      </p>
      <div class="btn-row">
        <button class="btn" :disabled="checkingTool" @click="checkToolUpdate">
          {{ checkingTool ? '检查中…' : '检查工具更新' }}
        </button>
        <a
          v-if="toolUpdate?.releaseUrl"
          class="btn ghost"
          :href="toolUpdate.releaseUrl"
          target="_blank"
          rel="noopener"
        >打开 Release</a>
      </div>
      <div v-if="toolUpdate" class="update-result">
        <div class="stat-row">
          <span>状态</span>
          <strong :class="toolStatusClass">{{ toolStatusText }}</strong>
        </div>
        <div class="stat-row">
          <span>当前版本</span>
          <strong class="mono">{{ toolUpdate.currentVersion }}</strong>
        </div>
        <div class="stat-row">
          <span>最新版本</span>
          <strong class="mono">{{ toolUpdate.latestVersion || '-' }}</strong>
        </div>
        <p class="meta" style="margin-top: var(--space-2)">{{ toolUpdate.message }}</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, rememberedPasswordKey, type ServerSummary } from '../api'

const router = useRouter()
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const servers = ref<ServerSummary[]>([])
const loading = ref(false)
const error = ref('')
const toolVersion = ref('')
const checkingTool = ref(false)
const toolUpdate = ref<Awaited<ReturnType<typeof api.checkToolUpdate>> | null>(null)

const toolStatusText = computed(() => {
  const r = toolUpdate.value
  if (!r) return ''
  if (!r.checked) return '检查未完成'
  return r.updateAvailable ? '有可用更新' : '已是最新'
})

const toolStatusClass = computed(() => {
  const r = toolUpdate.value
  if (!r?.checked) return 'warn-text'
  return r.updateAvailable ? 'warn-text' : 'ok-text'
})

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

async function loadVersion() {
  try {
    toolVersion.value = (await api.systemVersion()).version
  } catch {
    toolVersion.value = '-'
  }
}

async function checkToolUpdate() {
  checkingTool.value = true
  try {
    toolUpdate.value = await api.checkToolUpdate()
    if (toolUpdate.value.currentVersion)
      toolVersion.value = toolUpdate.value.currentVersion
    if (toolUpdate.value.checked) {
      toast(toolUpdate.value.updateAvailable ? '发现工具新版本' : '工具已是最新')
    } else {
      toast(toolUpdate.value.message || '检查未完成', 'error')
    }
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    checkingTool.value = false
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

onMounted(() => {
  void load()
  void loadVersion()
})
</script>

<style scoped>
.tool-update {
  margin-top: var(--space-5);
}

.update-result {
  margin-top: var(--space-4);
  padding-top: var(--space-3);
  border-top: 1px solid var(--border);
}

.ok-text { color: var(--success); }
.warn-text { color: var(--warning); }
</style>
