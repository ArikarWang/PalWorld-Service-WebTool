<template>
  <div>
    <div class="toolbar">
      <h1>定时任务</h1>
      <button class="btn primary" @click="show = !show">添加</button>
      <button class="btn" @click="load">刷新</button>
    </div>

    <div v-if="show" class="panel">
      <label>名称<input v-model="form.name" type="text" /></label>
      <label>类型
        <select v-model="form.type">
          <option value="announce">公告</option>
          <option value="save">保存</option>
          <option value="shutdown">关闭</option>
          <option value="backup">备份</option>
        </select>
      </label>
      <label>Cron<input v-model="form.cron" type="text" placeholder="0 4 * * *" /></label>
      <label>消息<input v-model="form.message" type="text" /></label>
      <button class="btn primary" @click="add">保存任务</button>
    </div>

    <table class="table">
      <thead><tr><th>名称</th><th>类型</th><th>Cron</th><th>上次结果</th><th>操作</th></tr></thead>
      <tbody>
        <tr v-for="t in tasks" :key="t.id">
          <td>{{ t.name }}</td>
          <td>{{ t.type }}</td>
          <td><code>{{ t.cron }}</code></td>
          <td>{{ t.lastResult || '-' }}</td>
          <td><button class="btn sm danger" @click="remove(t.id)">删除</button></td>
        </tr>
      </tbody>
    </table>
    <p v-if="!tasks.length" class="empty">暂无定时任务</p>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const tasks = ref<any[]>([])
const show = ref(false)
const form = reactive({ name: '', type: 'save', cron: '0 4 * * *', message: '', enabled: true })

async function load() {
  try { tasks.value = await api.schedules(id()) }
  catch (e: any) { toast(e.message, 'error') }
}

async function add() {
  try {
    await api.addSchedule(id(), { ...form })
    show.value = false
    form.name = ''
    toast('已添加')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

async function remove(taskId: string) {
  if (!confirm('删除该任务？')) return
  try {
    await api.deleteSchedule(id(), taskId)
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

onMounted(load)
</script>
