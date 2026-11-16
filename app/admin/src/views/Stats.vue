<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'

const stats = ref(null)
const sessions = ref([])
const prints = ref([])
const loading = ref(false)

async function refresh() {
  loading.value = true
  try {
    const [s, ses, pr] = await Promise.all([
      api.adminStats(),
      api.listSessions(),
      api.prints(500),
    ])
    stats.value = s
    sessions.value = Array.isArray(ses) ? ses : []
    prints.value = Array.isArray(pr) ? pr : []
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

const totalPhotos = computed(() => sessions.value.reduce((a, s) => a + (Number(s.photos) || 0), 0))
const totalBytes = computed(() => sessions.value.reduce((a, s) => a + (Number(s.bytes) || 0), 0))
const printCount = computed(() => prints.value.filter(p => p.success).length)
const maxPhotos = computed(() => Math.max(1, ...sessions.value.map(s => Number(s.photos) || 0)))

const daily = computed(() => {
  const list = [...sessions.value].reverse().slice(-14)
  return list
})

function fmtMB(bytes) {
  return (Number(bytes || 0) / 1048576).toFixed(1) + ' MB'
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Thống kê</h1>
    <p class="page-sub">Tổng quan vận hành: ảnh chụp, in ấn, cấu hình.</p>

    <a-spin :spinning="loading">
      <div class="stat-grid" style="margin-bottom: 16px;">
        <div class="stat-card"><div class="stat-label">Tổng ảnh đã chụp</div><div class="stat-value stat-accent">{{ totalPhotos }}</div></div>
        <div class="stat-card"><div class="stat-label">Tổng dung lượng</div><div class="stat-value">{{ fmtMB(totalBytes) }}</div></div>
        <div class="stat-card"><div class="stat-label">Số ngày chụp</div><div class="stat-value">{{ sessions.length }}</div></div>
        <div class="stat-card"><div class="stat-label">Lần in thành công</div><div class="stat-value">{{ printCount }}</div></div>
        <div class="stat-card"><div class="stat-label">Frame trong kho</div><div class="stat-value">{{ stats?.frames ?? '—' }}</div></div>
        <div class="stat-card"><div class="stat-label">Layout</div><div class="stat-value">{{ stats?.layouts ?? '—' }}</div></div>
      </div>

      <div class="card" style="margin-bottom: 16px;">
        <div class="stat-label" style="margin-bottom: 10px;">Ảnh theo ngày (14 ngày gần nhất)</div>
        <div v-if="daily.length === 0" class="empty" style="padding: 24px 0;">Chưa có dữ liệu.</div>
        <div v-else class="chart">
          <div v-for="d in daily" :key="d.date" class="chart-col">
            <div class="chart-bar" :style="{ height: Math.max(4, (Number(d.photos) / maxPhotos) * 100) + '%' }">
              <span class="chart-value">{{ d.photos }}</span>
            </div>
            <div class="chart-label">{{ d.date.slice(5, 10) }}</div>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="stat-label" style="margin-bottom: 10px;">Thông tin máy</div>
        <a-descriptions :column="2" size="small" bordered>
          <a-descriptions-item label="Camera backend">{{ stats?.cameraBackend ?? '—' }}</a-descriptions-item>
          <a-descriptions-item label="Máy in">{{ stats?.printerAvailable ? 'Sẵn sàng' : 'Không thấy' }}</a-descriptions-item>
          <a-descriptions-item label="Bridge">v{{ stats?.version ?? '—' }}</a-descriptions-item>
          <a-descriptions-item label="Frame / Layout / Danh mục">{{ stats?.frames ?? 0 }} / {{ stats?.layouts ?? 0 }} / {{ stats?.categories ?? 0 }}</a-descriptions-item>
        </a-descriptions>
      </div>
    </a-spin>
  </div>
</template>
