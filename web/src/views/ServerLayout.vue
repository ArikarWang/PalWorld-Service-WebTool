<template>
  <div class="layout">
    <aside class="side">
      <div class="side-head">
        <h2>{{ name || id }}</h2>
        <p class="meta mono">{{ id }}</p>
      </div>
      <nav>
        <router-link :to="`/servers/${id}/dashboard`">总览</router-link>
        <router-link :to="`/servers/${id}/players`">玩家</router-link>
        <router-link :to="`/servers/${id}/control`">控制</router-link>
        <router-link :to="`/servers/${id}/config`">配置</router-link>
        <router-link :to="`/servers/${id}/logs`">日志</router-link>
        <router-link :to="`/servers/${id}/backup`">备份</router-link>
        <router-link :to="`/servers/${id}/schedules`">计划任务</router-link>
      </nav>
      <div class="side-footer">
        <button class="btn ghost sm" @click="logout">退出登录</button>
        <router-link class="btn ghost sm" to="/">服务器列表</router-link>
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

<style scoped>
.side-head {
  display: grid;
  gap: 0.35rem;
  padding: 0 var(--space-2);
}

.side-head h2 {
  margin: 0;
}
</style>
