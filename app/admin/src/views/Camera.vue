<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'

const step = ref(1) // 1 = chọn hãng, 2 = chọn thiết bị
const backend = ref('webcam')
const cam = ref(null)
const devices = ref([])
const selectedDevice = ref(0)
const busy = ref(false)
const log = ref('')

async function refresh() {
  try { cam.value = await api.cameraStatus() } catch (e) { message.error(String(e.message || e)) }
}

async function refreshDevices() {
  busy.value = true
  try {
    devices.value = await api.devices()
    if (devices.value.length) selectedDevice.value = devices.value[0].index
    if (devices.value.length === 0) message.info('Không tìm thấy thiết bị video nào. Kiểm tra cáp USB / quyền camera của Windows.')
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    busy.value = false
  }
}

async function selectBackend(b) {
  backend.value = b
  await applyBackend()
}

async function applyBackend() {
  busy.value = true
  try {
    const body = backend.value === 'webcam'
      ? { backend: 'webcam', device: Number(selectedDevice.value) || 0 }
      : backend.value === 'canon'
        ? { backend: 'canon', device: 0 }
        : { backend: 'fake', device: 0 }
    const r = await api.switchCamera(body)
    cam.value = r
    message.success(`Đã chuyển sang: ${r?.backend ?? backend.value}`)
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    busy.value = false
  }
}

async function useDevice(index) {
  selectedDevice.value = index
  backend.value = 'webcam'
  await applyBackend()
}

async function connect() {
  busy.value = true
  try { await api.cameraConnect(); await refresh(); message.success('Đã kết nối máy ảnh.') } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false }
}

async function disconnect() {
  busy.value = true
  try { await api.cameraDisconnect(); await refresh(); message.success('Đã ngắt kết nối.') } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false }
}

async function capture() {
  busy.value = true
  try {
    const r = await api.cameraCapture()
    if (r?.Success || r?.success || r?.FilePath || r?.filePath) message.success('Đã chụp — ảnh lưu trong mục Phiên chụp.')
    else message.warning('Bridge phản hồi nhưng không rõ ảnh chụp: ' + JSON.stringify(r))
  } catch (e) { message.error(String(e.message || e)) } finally { busy.value = false }
}

const backendLabel = computed(() => cam.value?.backend ?? backend.value)
const isConnected = computed(() => cam.value?.connected ?? cam.value?.Connected ?? false)
const camModel = computed(() => cam.value?.cameraModel ?? cam.value?.CameraModel ?? '—')

const vendorOptions = [
  { id: 'canon', title: 'Canon EOS', sub: 'Canon EDSDK qua USB — chụp full-res', disabled: false },
  { id: 'fujifilm', title: 'Fujifilm', sub: 'Cần Fujifilm SDK — chưa hỗ trợ', disabled: true },
  { id: 'webcam', title: 'Webcam / Card', sub: 'Logitech C930e hoặc HDMI capture card', disabled: false },
]

onMounted(async () => {
  await refresh()
  await refreshDevices()
})
</script>

<template>
  <div>
    <h1 class="page-title">Quản lý máy ảnh</h1>
    <p class="page-sub">Hỗ trợ Canon EDSDK (ảnh full-res) và Webcam/Capture Card. Chọn loại máy ảnh trước khi chụp.</p>

    <div class="card" style="display: flex; flex-direction: column; gap: 16px; max-width: 780px;">
      <a-alert
        type="warning"
        show-icon
        :message="'Lưu ý: app CamRent Booth.exe (bản production) đang chiếm C930e. Muốn ClickFun capture qua webcam, phải đóng CamRent Booth trước — hoặc test trên máy booth với Canon.'"
      />
      <div class="stat-label">Hãng máy ảnh</div>
      <div class="vendor-grid">
        <button v-for="v in vendorOptions" :key="v.id" class="vendor-card" :class="{ active: backend === v.id, disabled: v.disabled }" :disabled="v.disabled" @click="selectBackend(v.id)">
          <span class="vendor-icon">{{ v.id === 'canon' ? '📷' : v.id === 'fujifilm' ? '🎞️' : '🖥️' }}</span>
          <span class="vendor-title">{{ v.title }}</span>
          <span class="vendor-sub">{{ v.sub }}</span>
        </button>
      </div>

      <div>
        <div class="stat-label" style="display: flex; align-items: center; justify-content: space-between;">
          Thiết bị phát hiện
          <a-button size="small" :loading="busy" @click="refreshDevices">↻ Quét lại</a-button>
        </div>
        <div v-if="devices.length === 0" class="empty" style="padding: 18px 0;">
          Không tìm thấy thiết bị video. (Máy này chỉ có Canon EDSDK qua USB là đủ chụp — không cần UVC.)
        </div>
        <div v-else class="device-list">
          <button v-for="d in devices" :key="d.index" class="device-row" :class="{ active: backend === 'webcam' && Number(selectedDevice) === Number(d.index) }" @click="useDevice(d.index)">
            <span class="device-index">#{{ d.index }}</span>
            <span class="device-name">{{ d.name }}</span>
            <span class="device-res">{{ d.width && d.height ? `${d.width}×${d.height}` : '—' }}</span>
          </button>
        </div>
        <p class="frame-meta" style="margin-top: 8px;">Thiết bị đầu tiên = index 0 (VD: C930e). Click vào thiết bị để chuyển sang webcam backend + tự kết nối.</p>
      </div>

      <a-divider />

      <div>
        <div class="stat-label">Trạng thái</div>
        <p style="margin: 8px 0 2px;"><strong>Backend:</strong> {{ backendLabel }}</p>
        <p style="margin: 2px 0;"><strong>Model:</strong> {{ camModel }}</p>
        <p style="margin: 2px 0;"><strong>Kết nối:</strong> {{ isConnected ? 'Có ✅' : 'Chưa' }}</p>
      </div>

      <a-space wrap>
        <a-button type="primary" :loading="busy" @click="connect">Kết nối</a-button>
        <a-button :loading="busy" @click="disconnect">Ngắt kết nối</a-button>
        <a-button :loading="busy" @click="capture">Chụp thử</a-button>
        <a-button @click="refresh">Làm mới</a-button>
      </a-space>
    </div>
  </div>
</template>
