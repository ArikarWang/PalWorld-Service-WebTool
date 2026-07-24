<template>
  <div class="app-shell">
    <header v-if="!isOffline" class="global-bar">
      <router-link to="/" class="brand">
        PalWorld Service
        <small v-if="appVersion" class="brand-ver">v{{ appVersion }}</small>
      </router-link>
      <div class="bar-actions">
        <button
          class="theme-toggle"
          type="button"
          :title="theme === 'light' ? '切换深色主题' : '切换浅色主题'"
          @click="toggleTheme"
        >
          {{ theme === 'light' ? '◐' : '◑' }}
        </button>
        <button class="btn danger sm" @click="shutdown">关闭管理服务</button>
      </div>
    </header>
    <header v-else class="global-bar">
      <span class="brand">
        PalWorld Service
        <small v-if="appVersion" class="brand-ver">v{{ appVersion }}</small>
      </span>
      <div class="bar-actions">
        <button
          class="theme-toggle"
          type="button"
          :title="theme === 'light' ? '切换深色主题' : '切换浅色主题'"
          @click="toggleTheme"
        >
          {{ theme === 'light' ? '◐' : '◑' }}
        </button>
        <span class="meta">服务已停止</span>
      </div>
    </header>
    <router-view />
    <div v-if="toast" class="toast" :class="toast.type">{{ toast.message }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, provide, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from './api'
import { theme, toggleTheme } from './theme'

const route = useRoute()
const router = useRouter()
const toast = ref<{ message: string; type: string } | null>(null)
const appVersion = ref('')
const isOffline = computed(() => route.name === 'offline')
let healthTimer: number | undefined

function showToast(message: string, type = 'success') {
  toast.value = { message, type }
  setTimeout(() => (toast.value = null), 3000)
}

provide('toast', showToast)

async function checkHealth() {
  if (route.name === 'offline') return
  const recoveredAt = Number(sessionStorage.getItem('pal.recoveredAt') || 0)
  if (recoveredAt > 0 && Date.now() - recoveredAt < 10000) return
  try {
    await api.health()
  } catch {
    router.replace({ name: 'offline' })
  }
}

async function loadVersion() {
  try {
    appVersion.value = (await api.systemVersion()).version
  } catch {
    /* ignore while offline */
  }
}

async function shutdown() {
  if (!confirm('确定关闭管理服务？控制台窗口将自动关闭。')) return
  try {
    await api.shutdownService()
    showToast('正在关闭服务…')
    setTimeout(() => router.replace({ name: 'offline' }), 400)
  } catch {
    router.replace({ name: 'offline' })
  }
}

onMounted(() => {
  checkHealth()
  void loadVersion()
  healthTimer = window.setInterval(checkHealth, 4000)
})

onUnmounted(() => {
  if (healthTimer) clearInterval(healthTimer)
})
</script>
