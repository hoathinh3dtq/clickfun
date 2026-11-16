<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'

const stats = ref(null)
const printer = ref('')
const loading = ref(false)

async function refresh() {
  loading.value = true
  try {
    stats.value = await api.adminStats()
    printer.value = stats.value?.printerAvailable ? 'DS-RX1 (driver phát hiện OK)' : 'Không phát hiện máy in'
  } catch {
    printer.value = 'Không đọc được'
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Bản quyền &amp; máy</h1>
    <p class="page-sub">Thông tin máy &amp; trạng thái bản quyền của bản ClickFun này.</p>

    <div class="card" style="max-width: 720px; display: flex; flex-direction: column; gap: 6px;">
      <a-alert type="success" show-icon :message="'Bản quyền: Single-machine (theo yêu cầu chủ sở hữu) — không cần khóa kích hoạt.'" />
      <a-divider />
      <a-descriptions :column="1" bordered size="small">
        <a-descriptions-item label="Máy này">ClickFun booth — single PC</a-descriptions-item>
        <a-descriptions-item label="Chế độ">Fullscreen kiosk (hoặc <code>CLICKFUN_FULLSCREEN=1</code>)</a-descriptions-item>
        <a-descriptions-item label="Camera backend">{{ stats?.cameraBackend ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="Máy in">{{ printer }}</a-descriptions-item>
        <a-descriptions-item label="Bridge">ClickFun.CameraBridge v{{ stats?.version ?? '—' }} — http://127.0.0.1:8737</a-descriptions-item>
        <a-descriptions-item label="Dữ liệu">%ProgramData%\ClickFun (offline-asset rebuild)</a-descriptions-item>
        <a-descriptions-item label="Panel">Không đồng bộ — mọi dữ liệu nằm trên máy này</a-descriptions-item>
      </a-descriptions>
    </div>
  </div>
</template>
