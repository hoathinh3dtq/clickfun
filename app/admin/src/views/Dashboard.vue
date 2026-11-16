<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'
import { PictureOutlined, LayoutOutlined, TagsOutlined, AimOutlined, PrinterOutlined } from '@ant-design/icons-vue'

const stats = ref(null)
const camera = ref(null)
const loading = ref(false)

async function refresh() {
  loading.value = true
  try {
    stats.value = await api.adminStats()
    camera.value = await api.cameraStatus().catch(() => null)
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Tổng quan booth</h1>
    <p class="page-sub">Trạng thái local: frame, layout, camera, máy in.</p>

    <a-spin :spinning="loading && !stats">
      <div class="stat-grid" style="margin-bottom: 16px;">
        <div class="stat-card">
          <div class="stat-label"><PictureOutlined /> Frame</div>
          <div class="stat-value stat-accent">{{ stats?.frames ?? '—' }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label"><LayoutOutlined /> Layout</div>
          <div class="stat-value">{{ stats?.layouts ?? '—' }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label"><TagsOutlined /> Danh mục</div>
          <div class="stat-value">{{ stats?.categories ?? '—' }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-label"><AimOutlined /> Bộ lọc màu</div>
          <div class="stat-value">{{ stats?.filters ?? '—' }}</div>
        </div>
      </div>

      <div class="card" style="display: grid; grid-template-columns: 1fr 1fr; gap: 18px;">
        <div>
          <div class="stat-label" style="margin-bottom: 8px;">Máy ảnh</div>
          <p><strong>Backend:</strong> {{ camera?.Backend ?? stats?.cameraBackend ?? '—' }}</p>
          <p><strong>Đã kết nối:</strong> {{ camera?.Connected ? 'Có ✅' : 'Chưa' }}</p>
          <p v-if="camera?.CameraModel"><strong>Model:</strong> {{ camera.CameraModel }}</p>
          <p v-if="camera?.FreeBytesOnCard"><strong>Thẻ nhớ trống:</strong> {{ Math.round(camera.FreeBytesOnCard / 1048576) }} MB</p>
        </div>
        <div>
          <div class="stat-label" style="margin-bottom: 8px;">Máy in</div>
          <p><strong>Trạng thái:</strong> {{ stats?.printerAvailable ? 'Sẵn sàng ✅' : 'Không thấy máy in' }}</p>
          <p><strong>Phiên bản bridge:</strong> {{ stats?.version ?? '—' }}</p>
          <p v-if="stats?.updatedAtMs"><strong>Config cập nhật:</strong> {{ new Date(stats.updatedAtMs).toLocaleString('vi-VN') }}</p>
        </div>
      </div>

      <div style="margin-top: 14px; display: flex; gap: 10px;">
        <a-button type="primary" @click="refresh">Làm mới</a-button>
        <a-button @click="$router.push({ name: 'frames' })">Quản lý kho frame →</a-button>
        <a-button @click="$router.push({ name: 'layouts' })">Quản lý layout →</a-button>
      </div>
    </a-spin>
  </div>
</template>
