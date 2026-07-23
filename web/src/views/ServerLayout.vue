<template>
  <div class="layout">
    <aside class="side">
      <h2>{{ name || id }}</h2>
      <nav>
        <router-link :to="`/servers/${id}/dashboard`">仪表盘</router-link>
        <router-link :to="`/servers/${id}/players`">玩家</router-link>
        <router-link :to="`/servers/${id}/control`">控制</router-link>
        <router-link :to="`/servers/${id}/config`">配置</router-link>
        <router-link :to="`/servers/${id}/logs`">日志</router-link>
        <router-link :to="`/servers/${id}/backup`">备份</router-link>
        <router-link :to="`/servers/${id}/schedules`">定时任务</router-link>
      </nav>
      <div style="margin-top:1.5rem;padding:0 0.5rem">
        <button class="btn sm" @click="logout">退出登录</button>
        <p style="margin-top:0.75rem"><router-link to="/">服务器列表</router-link></p>
      </div>
    </aside>
    <main class="main">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api'

const props = defineProps<{ id: string }>()
const router = useRouter()
const name = ref('')

onMounted(async () => {
  try {
    const s = await api.getServer(props.id)
    name.value = s.name
  } catch { /* ignore */ }
})

async function logout() {
  await api.logout(props.id)
  router.push('/')
}
</script>
