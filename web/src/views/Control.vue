<template>
  <div>
    <h1 style="margin-bottom:1rem">服务器控制</h1>
    <div class="panel">
      <h3>公告</h3>
      <textarea v-model="message" rows="3" style="width:100%;margin:0.75rem 0" class="code-box" />
      <button class="btn primary" @click="announce">发送公告</button>
    </div>
    <div class="panel">
      <h3>快捷操作</h3>
      <div class="toolbar" style="margin-top:0.75rem">
        <button class="btn" @click="doSave">保存世界</button>
        <button class="btn" @click="doShutdown">优雅关闭</button>
        <button class="btn danger" @click="doStop">强制停止</button>
      </div>
    </div>
    <div class="panel">
      <h3>本机进程</h3>
      <p class="meta">状态：{{ running == null ? '未知' : (running ? '运行中' : '未运行') }}</p>
      <div class="toolbar" style="margin-top:0.75rem">
        <button class="btn" @click="checkProcess">刷新状态</button>
        <button class="btn primary" @click="startProcess">启动</button>
        <button class="btn danger" @click="stopProcess">结束进程</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { inject, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})
const message = ref('')
const running = ref<boolean | null>(null)

async function announce() {
  try {
    await api.announce(id(), message.value)
    toast('公告已发送')
    message.value = ''
  } catch (e: any) { toast(e.message, 'error') }
}

async function doSave() {
  try { await api.save(id()); toast('已保存') }
  catch (e: any) { toast(e.message, 'error') }
}

async function doShutdown() {
  if (!confirm('确定优雅关闭帕鲁服务器？')) return
  try { await api.shutdown(id(), 60, '服务器即将关闭'); toast('已发送关闭') }
  catch (e: any) { toast(e.message, 'error') }
}

async function doStop() {
  if (!confirm('确定强制停止？')) return
  try { await api.stop(id()); toast('已停止') }
  catch (e: any) { toast(e.message, 'error') }
}

async function checkProcess() {
  try { running.value = (await api.process(id())).running }
  catch (e: any) { toast(e.message, 'error') }
}

async function startProcess() {
  try { await api.processStart(id()); toast('已启动'); await checkProcess() }
  catch (e: any) { toast(e.message, 'error') }
}

async function stopProcess() {
  if (!confirm('结束本机 PalServer 进程？')) return
  try { await api.processStop(id()); toast('已结束'); await checkProcess() }
  catch (e: any) { toast(e.message, 'error') }
}

onMounted(checkProcess)
</script>
