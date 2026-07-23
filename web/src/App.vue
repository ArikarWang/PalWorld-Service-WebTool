<template>
  <div class="app-shell">
    <header class="global-bar">
      <router-link to="/" class="brand">PalWorld Service</router-link>
      <button class="btn danger sm" @click="shutdown">关闭管理服务</button>
    </header>
    <router-view />
    <div v-if="toast" class="toast" :class="toast.type">{{ toast.message }}</div>
  </div>
</template>

<script setup lang="ts">
import { provide, ref } from 'vue'
import { api } from './api'

const toast = ref<{ message: string; type: string } | null>(null)

function showToast(message: string, type = 'success') {
  toast.value = { message, type }
  setTimeout(() => (toast.value = null), 3000)
}

provide('toast', showToast)

async function shutdown() {
  if (!confirm('确定关闭管理服务？控制台窗口将退出。')) return
  try {
    await api.shutdownService()
    showToast('正在关闭服务…')
  } catch (e: any) {
    showToast(e.message, 'error')
  }
}
</script>
