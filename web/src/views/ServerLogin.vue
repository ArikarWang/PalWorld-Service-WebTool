<template>
  <div class="form-card">
    <h2>登录：{{ serverName || id }}</h2>
    <p class="hint">输入该服的网页登录密码（webPassword）</p>
    <form @submit.prevent="submit">
      <label>
        密码
        <input v-model="password" type="password" autocomplete="current-password" required autofocus />
      </label>
      <label class="check">
        <input v-model="remember" type="checkbox" />
        记住密码
      </label>
      <button class="btn primary" type="submit" :disabled="loading" style="width:100%">登录</button>
      <p v-if="error" class="error">{{ error }}</p>
    </form>
    <p style="margin-top:1rem"><router-link to="/">← 返回列表</router-link></p>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, rememberedPasswordKey } from '../api'

const props = defineProps<{ id: string }>()
const route = useRoute()
const router = useRouter()
const password = ref('')
const remember = ref(true)
const loading = ref(false)
const error = ref('')
const serverName = ref('')

onMounted(async () => {
  password.value = localStorage.getItem(rememberedPasswordKey(props.id)) || ''
  try {
    const s = await api.getServer(props.id)
    serverName.value = s.name
  } catch { /* ignore */ }
})

async function submit() {
  loading.value = true
  error.value = ''
  try {
    await api.login(props.id, password.value)
    if (remember.value)
      localStorage.setItem(rememberedPasswordKey(props.id), password.value)
    else
      localStorage.removeItem(rememberedPasswordKey(props.id))
    const redirect = (route.query.redirect as string) || `/servers/${props.id}/dashboard`
    router.replace(redirect)
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}
</script>
