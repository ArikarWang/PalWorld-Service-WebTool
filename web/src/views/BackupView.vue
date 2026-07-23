<template>
  <div>
    <div class="toolbar">
      <h1>存档备份</h1>
      <button class="btn primary" @click="create">创建备份</button>
      <button class="btn" @click="load">刷新</button>
    </div>
    <table class="table">
      <thead><tr><th>文件</th><th>大小</th><th>时间</th><th>操作</th></tr></thead>
      <tbody>
        <tr v-for="b in backups" :key="b.fileName">
          <td>{{ b.fileName }}</td>
          <td>{{ formatSize(b.sizeBytes) }}</td>
          <td>{{ formatDate(b.createdAtUtc) }}</td>
          <td><button class="btn sm" @click="restore(b.fileName)">恢复</button></td>
        </tr>
      </tbody>
    </table>
    <p v-if="!backups.length" class="empty">暂无备份</p>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const backups = ref<any[]>([])

async function load() {
  try { backups.value = await api.backups(id()) }
  catch (e: any) { toast(e.message, 'error') }
}

async function create() {
  try {
    await api.createBackup(id())
    toast('备份完成')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

async function restore(fileName: string) {
  if (!confirm('恢复将覆盖当前存档，确定？')) return
  try {
    await api.restoreBackup(id(), fileName)
    toast('已恢复')
  } catch (e: any) { toast(e.message, 'error') }
}

function formatSize(n: number) {
  if (n < 1024) return n + ' B'
  if (n < 1048576) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1048576).toFixed(1) + ' MB'
}
function formatDate(iso: string) {
  return new Date(iso).toLocaleString('zh-CN')
}

onMounted(load)
</script>
