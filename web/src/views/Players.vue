<template>
  <div>
    <div class="toolbar">
      <h1>玩家</h1>
      <button class="btn" @click="load">刷新</button>
    </div>
    <table class="table">
      <thead>
        <tr><th>名称</th><th>UserId</th><th>IP</th><th>Ping</th><th>等级</th><th>操作</th></tr>
      </thead>
      <tbody>
        <tr v-for="p in players" :key="p.userId">
          <td>{{ p.name }}</td>
          <td><code>{{ p.userId }}</code></td>
          <td>{{ p.ip }}</td>
          <td>{{ p.ping }}</td>
          <td>{{ p.level }}</td>
          <td>
            <button class="btn sm danger" @click="kick(p.userId)">踢出</button>
            <button class="btn sm danger" @click="ban(p.userId)">封禁</button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="!players.length" class="empty">暂无在线玩家</p>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const players = ref<any[]>([])

async function load() {
  try { players.value = await api.players(id()) }
  catch (e: any) { toast(e.message, 'error') }
}

async function kick(userId: string) {
  const message = prompt('踢出原因（可选）') || ''
  try {
    await api.kick(id(), userId, message)
    toast('已踢出')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

async function ban(userId: string) {
  const message = prompt('封禁原因（可选）') || ''
  try {
    await api.ban(id(), userId, message)
    toast('已封禁')
    await load()
  } catch (e: any) { toast(e.message, 'error') }
}

onMounted(load)
</script>
