<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'

const cam = ref(null)
const printInfo = ref(null)
const busy = ref(false)

async function refresh() {
  try { cam.value = await api.cameraStatus() } catch (e) { message.error(String(e.message || e)) }
  try { printInfo.value = await api.printLayouts() } catch { printInfo.value = null }
}

async function connect() {
  busy.value = true
  try { await api.cameraConnect(); message.success('Đã kết nối máy ảnh.') } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false; await refresh() }
}

async function disconnect() {
  busy.value = true
  try { await api.cameraDisconnect(); message.success('Đã ngắt kết nối.') } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false; await refresh() }
}

async function capture() {
  busy.value = true
  try {
    const r = await api.cameraCapture()
    if (r?.Success || r?.success || r?.FilePath || r?.filePath) message.success('Đã chụp ảnh.')
    else message.warning('Bridge phản hồi nhưng không rõ ảnh chụp: ' + JSON.stringify(r))
  } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false }
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Máy ảnh &amp; máy in</h1>
    <p class="page-sub">Kết nối Canon EDSDK, chụp thử, kiểm tra máy in DS-RX1.</p>

    <div class="card" style="max-width: 720px; display: flex; flex-direction: column; gap: 14px;">
      <div>
        <div class="stat-label">Máy ảnh (hiện tại: {{ cam?.Backend ?? '—' }})</div>
        <p style="margin: 8px 0 0;"><strong>Kết nối:</strong> {{ cam?.Connected ? 'Có ✅' : 'Chưa' }}<span v-if="cam?.CameraModel"> · {{ cam.CameraModel }}</span></p>
        <p v-if="cam?.FreeBytesOnCard" style="margin: 4px 0 0; color: var(--cf-muted);">Thẻ nhớ trống: {{ Math.round(cam.FreeBytesOnCard / 1048576) }} MB</p>
      </div>
      <a-space>
        <a-button type="primary" :loading="busy" @click="connect">Kết nối</a-button>
        <a-button :loading="busy" @click="disconnect">Ngắt kết nối</a-button>
        <a-button :loading="busy" @click="capture">Chụp thử</a-button>
        <a-button @click="refresh">Làm mới</a-button>
      </a-space>

      <a-divider />

      <div>
        <div class="stat-label">Máy in (DS-RX1)</div>
        <p style="margin: 8px 0 0;"><strong>Máy in:</strong> {{ printInfo?.printer ?? '—' }}</p>
        <p style="margin: 4px 0 0; color: var(--cf-muted);"><strong>Khổ giấy nhận:</strong> {{ (printInfo?.layouts || []).join(', ') || 'không đọc được driver' }}</p>
        <p style="margin: 4px 0 0; font-size: 12px; color: var(--cf-muted);">Khổ giấy “auto” sẽ tự nhận 2×6/4×6 theo media thực tế khi in.</p>
      </div>

      <a-alert type="info" show-icon :message="'Muốn in thật, cắm máy in + chọn tên driver trong mục Cài đặt booth. In thử nên dùng ảnh khách chụp từ booth.'" />
    </div>
  </div>
</template>
