<template>
  <div>
    <div class="page-header">
      <h1>服务器控制</h1>
    </div>

    <div class="panel">
      <h3>公告</h3>
      <div class="panel-body">
        <textarea v-model="message" rows="3" class="code-box" placeholder="输入要发送给玩家的公告内容" />
        <div class="btn-row">
          <button class="btn primary" :disabled="busy || !message.trim()" @click="announce">发送公告</button>
        </div>
      </div>
    </div>

    <div class="panel">
      <h3>定时关服</h3>
      <p class="meta" style="margin-bottom: var(--space-3)">
        每种预设会连续发送 3 条公告，执行世界保存，再按对应倒计时关闭服务器。
      </p>
      <div class="preset-grid">
        <button class="btn" :disabled="busy" @click="shutdownPreset(10)">10 秒关服</button>
        <button class="btn" :disabled="busy" @click="shutdownPreset(30)">30 秒关服</button>
        <button class="btn" :disabled="busy" @click="shutdownPreset(60)">1 分钟关服</button>
      </div>
    </div>

    <div class="panel">
      <h3>快捷操作</h3>
      <div class="btn-row">
        <button class="btn" :disabled="busy" @click="doSave">保存世界</button>
        <button class="btn danger" :disabled="busy" @click="doStop">强制停止</button>
      </div>
    </div>

    <div class="panel">
      <h3>本机进程</h3>
      <p class="meta">状态：{{ running == null ? '未知' : (running ? '运行中' : '未运行') }}</p>
      <div class="btn-row" style="margin-top: var(--space-3)">
        <button class="btn ghost" :disabled="busy" @click="checkProcess">刷新状态</button>
        <button class="btn primary" :disabled="busy || running === true" @click="startProcess">启动</button>
        <button class="btn danger" :disabled="busy || running === false" @click="stopProcess">结束进程</button>
      </div>
    </div>

    <div class="panel">
      <h3>检查更新</h3>
      <p class="meta" style="margin-bottom: var(--space-3)">
        对比本机 Steam 安装清单与远端 public 分支 buildid（默认 AppId 2394010）。仅检查，不会自动更新。
      </p>
      <div class="btn-row">
        <button class="btn" :disabled="busy || checkingUpdate" @click="checkUpdate">
          {{ checkingUpdate ? '检查中…' : '检查帕鲁服务更新' }}
        </button>
      </div>
      <div v-if="updateResult" class="update-result">
        <div class="stat-row">
          <span>状态</span>
          <strong :class="updateStatusClass">{{ updateStatusText }}</strong>
        </div>
        <div class="stat-row">
          <span>本地 BuildId</span>
          <strong class="mono">{{ updateResult.localBuildId || '-' }}</strong>
        </div>
        <div class="stat-row">
          <span>远端 BuildId</span>
          <strong class="mono">{{ updateResult.remoteBuildId || '-' }}</strong>
        </div>
        <div class="stat-row">
          <span>来源</span>
          <strong>{{ updateResult.remoteSource || '-' }}</strong>
        </div>
        <p class="meta" style="margin-top: var(--space-2)">{{ updateResult.message }}</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const message = ref('')
const running = ref<boolean | null>(null)
const busy = ref(false)
const checkingUpdate = ref(false)
const updateResult = ref<Awaited<ReturnType<typeof api.checkUpdate>> | null>(null)

const presetLabels: Record<number, string> = {
  10: '10秒',
  30: '30秒',
  60: '1分钟',
}

const updateStatusText = computed(() => {
  const r = updateResult.value
  if (!r) return ''
  if (!r.checked) return '检查未完成'
  return r.updateAvailable ? '有可用更新' : '已是最新'
})

const updateStatusClass = computed(() => {
  const r = updateResult.value
  if (!r?.checked) return 'warn-text'
  return r.updateAvailable ? 'warn-text' : 'ok-text'
})

async function announce() {
  busy.value = true
  try {
    await api.announce(id(), message.value)
    toast('公告已发送')
    message.value = ''
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function doSave() {
  busy.value = true
  try {
    await api.save(id())
    toast('已保存')
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function shutdownPreset(seconds: number) {
  const label = presetLabels[seconds] || `${seconds}秒`
  if (!confirm(`确定执行「${label}关服」？将连发 3 条公告、保存世界，然后关闭服务器。`)) return
  busy.value = true
  try {
    await api.shutdownPreset(id(), seconds)
    toast(`已启动 ${label}关服流程`)
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function doStop() {
  if (!confirm('确定强制停止？')) return
  busy.value = true
  try {
    await api.stop(id())
    toast('已停止')
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function checkProcess() {
  try {
    running.value = (await api.process(id())).running
  } catch (e: any) {
    toast(e.message, 'error')
  }
}

async function startProcess() {
  if (running.value) return
  busy.value = true
  try {
    await api.processStart(id())
    toast('已启动')
    await checkProcess()
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function stopProcess() {
  if (!confirm('结束本机 PalServer 进程？')) return
  busy.value = true
  try {
    await api.processStop(id())
    toast('已结束')
    await checkProcess()
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function checkUpdate() {
  checkingUpdate.value = true
  busy.value = true
  try {
    updateResult.value = await api.checkUpdate(id())
    if (updateResult.value.checked) {
      toast(updateResult.value.updateAvailable ? '发现可用更新' : '已是最新版本')
    } else {
      toast(updateResult.value.message || '检查未完成', 'error')
    }
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    checkingUpdate.value = false
    busy.value = false
  }
}

onMounted(checkProcess)
</script>

<style scoped>
.update-result {
  margin-top: var(--space-4);
  padding-top: var(--space-3);
  border-top: 1px solid var(--border);
}

.ok-text { color: var(--success); }
.warn-text { color: var(--warning); }
</style>
