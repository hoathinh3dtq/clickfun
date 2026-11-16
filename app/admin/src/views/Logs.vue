<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'
import { ReloadOutlined } from '@ant-design/icons-vue'

const lines = ref([])
const loading = ref(false)
const tail = ref(150)

async function refresh() {
  loading.value = true
  try {
    lines.value = await api.logs(Number(tail.value) || 150)
  } catch (e) {
    message.error(String(e.message || e))
    lines.value = []
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Nhật ký bridge</h1>
    <p class="page-sub">Log file hôm nay — chẩn đoán máy ảnh/máy in/sync.</p>

    <div class="toolbar">
      <a-select v-model:value="tail" style="width: 140px;">
        <a-select-option :value="50">50 dòng</a-select-option>
        <a-select-option :value="150">150 dòng</a-select-option>
        <a-select-option :value="300">300 dòng</a-select-option>
        <a-select-option :value="500">500 dòng</a-select-option>
      </a-select>
      <div class="spacer" />
      <a-button @click="refresh"><ReloadOutlined /> Làm mới</a-button>
    </div>

    <a-spin :spinning="loading">
      <div v-if="lines.length === 0" class="empty">Chưa có log hôm nay.</div>
      <pre v-else class="log-view">{{ lines.join('\n') }}</pre>
    </a-spin>
  </div>
</template>
