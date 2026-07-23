<template>
  <div>
    <div class="toolbar">
      <h1>日志</h1>
      <button class="btn" @click="load">刷新</button>
    </div>
    <pre class="log-box">{{ text || '暂无日志' }}</pre>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const text = ref('')

async function load() {
  try {
    const lines = await api.logs(id())
    text.value = lines.join('\n')
  } catch (e: any) { toast(e.message, 'error') }
}

onMounted(load)
</script>
