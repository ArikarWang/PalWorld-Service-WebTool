<template>
  <div>
    <div class="toolbar">
      <button class="btn sm" @click="$router.push(`/servers/${serverId}/players`)">← 玩家列表</button>
      <h1>帕鲁列表</h1>
    </div>
    <p class="meta">玩家：{{ displayName }}</p>

    <div class="panel" style="margin-top:1rem">
      <h3>可行性说明</h3>
      <p class="hint" style="margin:0.5rem 0 0">{{ result?.message || '加载中…' }}</p>
      <ul class="hint" style="margin-top:0.75rem;padding-left:1.2rem;line-height:1.7">
        <li>官方 REST：仅在线玩家，无帕鲁背包/潜能</li>
        <li>GameData API：世界坐标快照，非完整帕鲁箱与潜能评分</li>
        <li>完整方案：解析 SaveGames 玩家存档（1.0 Oodle），后续版本实现</li>
      </ul>
    </div>

    <div v-if="result?.pals?.length" class="card-grid" style="margin-top:1rem">
      <div v-for="(pal, i) in result.pals" :key="i" class="panel">
        <h3>{{ pal.nickname || pal.name || pal.characterId || 'Pal' }}</h3>
        <div class="stat-row"><span>等级</span><strong>{{ pal.level || '-' }}</strong></div>
        <div class="stat-row">
          <span>潜能评分</span>
          <strong class="potential">{{ pal.potentialLabel || pal.potentialScore || '-' }}</strong>
        </div>
      </div>
    </div>
    <p v-else class="empty">暂无帕鲁数据可展示</p>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const serverId = computed(() => route.params.id as string)
const playerKey = computed(() => route.params.playerKey as string)
const displayName = computed(() =>
  (route.query.name as string) || (route.query.userId as string) || playerKey.value)

const result = ref<any>(null)

onMounted(async () => {
  try {
    result.value = await api.playerPals(serverId.value, playerKey.value)
  } catch (e: any) {
    toast(e.message, 'error')
  }
})
</script>

<style scoped>
.potential { color: var(--warning); }
</style>
