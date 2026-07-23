<template>
  <div>
    <div class="toolbar">
      <h1>配置文件</h1>
      <button class="btn" @click="load">加载</button>
      <button class="btn primary" @click="save">保存</button>
    </div>
    <p class="hint">读取/写入 configPath（PalWorldSettings.ini）。修改后通常需重启帕鲁服务器。</p>
    <textarea v-model="content" class="code-box" rows="22" />
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const content = ref('')

async function load() {
  try {
    content.value = (await api.getConfig(id())).content || ''
  } catch (e: any) { toast(e.message, 'error') }
}

async function save() {
  try {
    await api.putConfig(id(), content.value)
    toast('配置已保存')
  } catch (e: any) { toast(e.message, 'error') }
}

onMounted(load)
</script>
