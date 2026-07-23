<template>
  <div class="app-shell">
    <header v-if="!isOffline" class="global-bar">
      <router-link to="/" class="brand">PalWorld Service</router-link>
      <button class="btn danger sm" @click="shutdown">关闭管理服务</button>
    </header>
    <header v-else class="global-bar">
      <span class="brand">PalWorld Service</span>
      <span class="meta">服务已停止</span>
    </header>
    <router-view />
    <div v-if="toast" class="toast" :class="toast.type">{{ toast.message }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, provide, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from './api'

const route = useRoute()
const router = useRouter()
const toast = ref<{ message: string; type: string } | null>(null)
const isOffline = computed(() => route.name === 'offline')
let healthTimer: number | undefined

function showToast(message: string, type = 'success') {
  toast.value = { message, type }
  setTimeout(() => (toast.value = null), 3000)
}

provide('toast', showToast)

async function checkHealth() {
  if (route.name === 'offline') return
  try {
    await api.health()
  } catch {
    router.replace({ name: 'offline' })
  }
}

async function shutdown() {
  if (!confirm('确定关闭管理服务？控制台窗口将自动关闭。')) return
  try {
    await api.shutdownService()
    showToast('正在关闭服务…')
    setTimeout(() => router.replace({ name: 'offline' }), 400)
  } catch (e: any) {
    // Service may already be dying
    router.replace({ name: 'offline' })
  }
}

onMounted(() => {
  checkHealth()
  healthTimer = window.setInterval(checkHealth, 4000)
})

onUnmounted(() => {
  if (healthTimer) clearInterval(healthTimer)
})
</script>
