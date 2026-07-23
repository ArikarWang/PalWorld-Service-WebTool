<template>
  <div class="offline-page">
    <div class="offline-card">
      <h1>管理服务已停止</h1>
      <p>无法连接到 PalWorld Service（通常因网页关闭服务或控制台窗口已退出）。</p>
      <p class="hint">
        浏览器<strong>无法安全地远程执行</strong> <code>start.bat</code>：
        管理服务停掉后已没有后端可接收「启动」指令；若另开常驻助手则违背「关窗即停」且权限风险高。
      </p>
      <p>请在 mini 主机上重新双击部署目录中的 <code>start.bat</code>，然后点击下方按钮。</p>
      <div class="toolbar" style="justify-content:center;margin-top:1.25rem">
        <button class="btn primary" @click="retry(true)" :disabled="checking || leaving">
          {{ checking || leaving ? '检测中…' : '重新检测连接' }}
        </button>
      </div>
      <p v-if="error && !leaving" class="error">{{ error }}</p>
      <p v-if="leaving" class="ok">已恢复，正在跳转…</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api'

const router = useRouter()
const route = useRoute()
const checking = ref(false)
const leaving = ref(false)
const error = ref('')
let timer: number | undefined
let disposed = false

function stopTimer() {
  if (timer !== undefined) {
    clearInterval(timer)
    timer = undefined
  }
}

async function retry(manual = false) {
  if (disposed || checking.value || leaving.value) return
  checking.value = true
  error.value = ''
  try {
    await api.health()
    if (disposed) return

    // Recovered: stop polling and leave this page
    leaving.value = true
    stopTimer()
    sessionStorage.setItem('pal.recoveredAt', String(Date.now()))
    await router.replace({ name: 'home' })

    // If still here (guard bounced us back), clear the sticky message
    if (!disposed && route.name === 'offline') {
      leaving.value = false
      error.value = manual
        ? '服务已响应，但页面跳转失败，请手动打开首页或刷新'
        : '服务已响应，稍后将重试跳转'
    }
  } catch {
    if (!disposed) {
      leaving.value = false
      error.value = '仍未检测到服务，请确认已运行 start.bat'
    }
  } finally {
    checking.value = false
  }
}

onMounted(() => {
  retry(false)
  timer = window.setInterval(() => retry(false), 5000)
})

onUnmounted(() => {
  disposed = true
  stopTimer()
})
</script>

<style scoped>
.offline-page {
  min-height: calc(100vh - 54px);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
}
.offline-card {
  max-width: 520px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1.75rem;
  text-align: center;
}
.offline-card h1 { margin-bottom: 0.75rem; font-size: 1.35rem; }
.offline-card p { color: var(--muted); line-height: 1.6; margin-bottom: 0.65rem; font-size: 0.95rem; }
.ok { color: var(--success) !important; }
</style>
