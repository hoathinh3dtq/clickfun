<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'

const form = ref({
  PrinterName: '',
  Copies: 1,
  Zoom: 0.98,
  OffsetX: 0,
  OffsetY: 0,
  CountdownSeconds: 3,
  DefaultFilter: 'none',
  OverlayTemplate: '',
  PhotosRoot: '',
  VideoMaxSeconds: 10,
  PaperLayout: 'auto',
})
const loading = ref(false)
const saving = ref(false)

async function refresh() {
  loading.value = true
  try {
    const s = await api.settings()
    form.value = { ...form.value, ...s }
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    await api.saveSettings({
      ...form.value,
      Copies: Number(form.value.Copies) || 1,
      Zoom: Number(form.value.Zoom) || 0.98,
      OffsetX: Number(form.value.OffsetX) || 0,
      OffsetY: Number(form.value.OffsetY) || 0,
      CountdownSeconds: Number(form.value.CountdownSeconds) || 3,
      VideoMaxSeconds: Number(form.value.VideoMaxSeconds) || 10,
    })
    message.success('Đã lưu cài đặt.')
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    saving.value = false
  }
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Cài đặt booth</h1>
    <p class="page-sub">Bridge đọc các giá trị này khi in/chụp/đếm ngược.</p>

    <a-spin :spinning="loading">
      <div class="card" style="max-width: 720px;">
        <a-form layout="vertical">
          <a-form-item label="Máy in (tên — để trống = mặc định hệ thống, VD: DNP DS-RX1)">
            <a-input v-model:value="form.PrinterName" placeholder="Để trống = default" />
          </a-form-item>
          <a-row :gutter="12">
            <a-col :span="8"><a-form-item label="Số bản in"><a-input-number v-model:value="form.Copies" :min="1" :max="20" style="width: 100%" /></a-form-item></a-col>
            <a-col :span="8"><a-form-item label="Zoom (0.90–1.00)"><a-input-number v-model:value="form.Zoom" :min="0.8" :max="1.05" :step="0.01" style="width: 100%" /></a-form-item></a-col>
            <a-col :span="4"><a-form-item label="Offset X (mm)"><a-input-number v-model:value="form.OffsetX" :min="-20" :max="20" style="width: 100%" /></a-form-item></a-col>
            <a-col :span="4"><a-form-item label="Offset Y (mm)"><a-input-number v-model:value="form.OffsetY" :min="-20" :max="20" style="width: 100%" /></a-form-item></a-col>
          </a-row>
          <a-row :gutter="12">
            <a-col :span="8"><a-form-item label="Đếm ngược (giây)"><a-input-number v-model:value="form.CountdownSeconds" :min="0" :max="30" style="width: 100%" /></a-form-item></a-col>
            <a-col :span="8"><a-form-item label="Video tối đa (giây)"><a-input-number v-model:value="form.VideoMaxSeconds" :min="1" :max="120" style="width: 100%" /></a-form-item></a-col>
            <a-col :span="8"><a-form-item label="Khổ giấy"><a-select v-model:value="form.PaperLayout"><a-select-option value="auto">Tự nhận (2×6/4×6)</a-select-option><a-select-option value="2x6">2×6</a-select-option><a-select-option value="4x6">4×6</a-select-option></a-select></a-form-item></a-col>
          </a-row>
          <a-form-item label="Filter mặc định">
            <a-select v-model:value="form.DefaultFilter">
              <a-select-option value="none">Không</a-select-option>
              <a-select-option value="bw">Đen trắng</a-select-option>
              <a-select-option value="sepia">Sepia</a-select-option>
              <a-select-option value="warm">Ấm</a-select-option>
              <a-select-option value="cool">Lạnh</a-select-option>
              <a-select-option value="vintage">Vintage</a-select-option>
            </a-select>
          </a-form-item>
          <a-form-item label="Thư mục lưu ảnh">
            <a-input v-model:value="form.PhotosRoot" placeholder="C:\ProgramData\ClickFun\photos" />
          </a-form-item>
          <a-space>
            <a-button type="primary" :loading="saving" @click="save">Lưu cài đặt</a-button>
            <a-button @click="refresh">Nạp lại</a-button>
          </a-space>
        </a-form>
      </div>
    </a-spin>
  </div>
</template>
