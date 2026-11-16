<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message, Modal } from 'ant-design-vue'
import { PlusOutlined, DeleteOutlined, ReloadOutlined } from '@ant-design/icons-vue'

const layouts = ref([])
const loading = ref(false)
const search = ref('')

const addOpen = ref(false)
const addBusy = ref(false)
const form = ref({ name: '', frame_shape: 'square', render_mode: '2x2', max_photos: 4, capture_count: 8, frame_count: 1, price: 50000, is_active: true, is_split: false })

const editOpen = ref(false)
const editBusy = ref(false)
const editForm = ref(null)

const filtered = computed(() => {
  if (!search.value.trim()) return layouts.value
  const q = search.value.trim().toLowerCase()
  return layouts.value.filter(l => String(l.name || '').toLowerCase().includes(q))
})

function money(v) {
  return Number(v || 0).toLocaleString('vi-VN') + 'đ'
}

async function refresh() {
  loading.value = true
  try { layouts.value = await api.listLayouts() } catch (e) { message.error(String(e.message || e)) } finally { loading.value = false }
}

async function submitAdd() {
  if (!form.value.name.trim()) return message.warning('Nhập tên layout.')
  addBusy.value = true
  try {
    await api.createLayout({ ...form.value, name: form.value.name.trim() })
    message.success('Đã tạo layout.')
    addOpen.value = false
    form.value = { name: '', frame_shape: 'square', render_mode: '2x2', max_photos: 4, capture_count: 8, frame_count: 1, price: 50000, is_active: true, is_split: false }
    await refresh()
  } catch (e) { message.error(String(e.message || e)) } finally { addBusy.value = false }
}

function openEdit(l) {
  editForm.value = { ...l }
  editOpen.value = true
}

async function submitEdit() {
  if (!editForm.value?.name?.trim()) return message.warning('Nhập tên layout.')
  editBusy.value = true
  try {
    await api.updateLayout(editForm.value.id, {
      name: editForm.value.name.trim(),
      frame_shape: editForm.value.frame_shape,
      render_mode: editForm.value.render_mode,
      layout_mode: editForm.value.render_mode,
      max_photos: Number(editForm.value.max_photos) || 4,
      capture_count: Number(editForm.value.capture_count) || 8,
      frame_count: Number(editForm.value.frame_count) || 1,
      price: Number(editForm.value.price) || 0,
      is_active: !!editForm.value.is_active,
      is_split: !!editForm.value.is_split,
    })
    message.success('Đã lưu.')
    editOpen.value = false
    await refresh()
  } catch (e) { message.error(String(e.message || e)) } finally { editBusy.value = false }
}

function remove(l) {
  Modal.confirm({
    title: `Xóa layout “${l.name}”?`,
    content: 'Booth sẽ không còn hiển thị layout này.',
    okText: 'Xóa',
    okType: 'danger',
    onOk: async () => {
      try { await api.deleteLayout(l.id); message.success('Đã xóa.'); await refresh() } catch (e) { message.error(String(e.message || e)) }
    },
  })
}

const columns = [
  { title: 'Tên', dataIndex: 'name', key: 'name' },
  { title: 'Kiểu', dataIndex: 'frame_shape', key: 'shape', width: 90 },
  { title: 'Chế độ', dataIndex: 'render_mode', key: 'render', width: 100 },
  { title: 'Số ảnh', dataIndex: 'max_photos', key: 'photos', width: 80 },
  { title: 'Giá', key: 'price', width: 120 },
  { title: 'Trạng thái', key: 'active', width: 110 },
  { title: '', key: 'actions', width: 150 },
]

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Layout in</h1>
    <p class="page-sub">Layout hiển thị cho khách chọn ở màn booth.</p>

    <div class="toolbar">
      <a-input v-model:value="search" placeholder="Tìm layout…" style="width: 240px;" allow-clear />
      <div class="spacer" />
      <a-button @click="refresh"><ReloadOutlined /> Làm mới</a-button>
      <a-button type="primary" @click="addOpen = true"><PlusOutlined /> Thêm layout</a-button>
    </div>

    <a-table :data-source="filtered" :loading="loading" :pagination="{ pageSize: 12 }" row-key="id" :columns="columns">
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'name'">
          <strong>{{ record.name }}</strong>
          <div class="frame-meta">#{{ record.id }} · {{ record.slug }}</div>
        </template>
        <template v-else-if="column.key === 'price'">{{ money(record.price) }}</template>
        <template v-else-if="column.key === 'active'">
          <a-tag :color="record.is_active ? 'green' : 'default'">{{ record.is_active ? 'Đang bật' : 'Tắt' }}</a-tag>
        </template>
        <template v-else-if="column.key === 'actions'">
          <a-space>
            <a-button size="small" @click="openEdit(record)">Sửa</a-button>
            <a-button size="small" danger @click="remove(record)"><DeleteOutlined /></a-button>
          </a-space>
        </template>
      </template>
    </a-table>

    <a-modal v-model:open="addOpen" title="Thêm layout" ok-text="Tạo" :confirm-loading="addBusy" @ok="submitAdd">
      <a-form layout="vertical">
        <a-form-item label="Tên layout" required><a-input v-model:value="form.name" placeholder="VD: 4 ảnh vuông" /></a-form-item>
        <a-row :gutter="12">
          <a-col :span="12">
            <a-form-item label="Kiểu frame">
              <a-select v-model:value="form.frame_shape">
                <a-select-option value="square">Vuông</a-select-option>
                <a-select-option value="grid">Lưới</a-select-option>
                <a-select-option value="strip">Strip</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="Chế độ render">
              <a-select v-model:value="form.render_mode">
                <a-select-option value="2x2">2×2</a-select-option>
                <a-select-option value="1x4">1×4 (strip)</a-select-option>
                <a-select-option value="grid">Lưới</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
        </a-row>
        <a-row :gutter="12">
          <a-col :span="8"><a-form-item label="Số ảnh"><a-input-number v-model:value="form.max_photos" :min="1" :max="16" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="Ảnh chụp"><a-input-number v-model:value="form.capture_count" :min="1" :max="30" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="Số khung"><a-input-number v-model:value="form.frame_count" :min="1" :max="10" style="width: 100%" /></a-form-item></a-col>
        </a-row>
        <a-row :gutter="12">
          <a-col :span="14"><a-form-item label="Giá (VNĐ)"><a-input-number v-model:value="form.price" :min="0" :step="5000" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="5"><a-form-item label="Bật"><a-switch v-model:checked="form.is_active" /></a-form-item></a-col>
          <a-col :span="5"><a-form-item label="Tách ảnh"><a-switch v-model:checked="form.is_split" /></a-form-item></a-col>
        </a-row>
      </a-form>
    </a-modal>

    <a-modal v-model:open="editOpen" title="Sửa layout" ok-text="Lưu" :confirm-loading="editBusy" @ok="submitEdit">
      <a-form v-if="editForm" layout="vertical">
        <a-form-item label="Tên layout" required><a-input v-model:value="editForm.name" /></a-form-item>
        <a-row :gutter="12">
          <a-col :span="12">
            <a-form-item label="Kiểu frame">
              <a-select v-model:value="editForm.frame_shape">
                <a-select-option value="square">Vuông</a-select-option>
                <a-select-option value="grid">Lưới</a-select-option>
                <a-select-option value="strip">Strip</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="Chế độ render">
              <a-select v-model:value="editForm.render_mode">
                <a-select-option value="2x2">2×2</a-select-option>
                <a-select-option value="1x4">1×4 (strip)</a-select-option>
                <a-select-option value="grid">Lưới</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
        </a-row>
        <a-row :gutter="12">
          <a-col :span="8"><a-form-item label="Số ảnh"><a-input-number v-model:value="editForm.max_photos" :min="1" :max="16" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="Ảnh chụp"><a-input-number v-model:value="editForm.capture_count" :min="1" :max="30" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="8"><a-form-item label="Số khung"><a-input-number v-model:value="editForm.frame_count" :min="1" :max="10" style="width: 100%" /></a-form-item></a-col>
        </a-row>
        <a-row :gutter="12">
          <a-col :span="14"><a-form-item label="Giá (VNĐ)"><a-input-number v-model:value="editForm.price" :min="0" :step="5000" style="width: 100%" /></a-form-item></a-col>
          <a-col :span="5"><a-form-item label="Bật"><a-switch v-model:checked="editForm.is_active" /></a-form-item></a-col>
          <a-col :span="5"><a-form-item label="Tách ảnh"><a-switch v-model:checked="editForm.is_split" /></a-form-item></a-col>
        </a-row>
      </a-form>
    </a-modal>
  </div>
</template>
