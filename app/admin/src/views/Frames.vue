<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message, Modal } from 'ant-design-vue'
import { PlusOutlined, DeleteOutlined, ReloadOutlined, UploadOutlined } from '@ant-design/icons-vue'

const frames = ref([])
const loading = ref(false)
const shapeFilter = ref('all')
const search = ref('')

const addOpen = ref(false)
const addForm = ref({ name: '', shape: 'square', holes: '' })
const addFile = ref(null)
const addBusy = ref(false)

const editOpen = ref(false)
const editForm = ref(null)
const editBusy = ref(false)

const filtered = computed(() => {
  let list = frames.value
  if (shapeFilter.value !== 'all') list = list.filter(f => f.frame === shapeFilter.value)
  if (search.value.trim()) {
    const q = search.value.trim().toLowerCase()
    list = list.filter(f => String(f.name || '').toLowerCase().includes(q))
  }
  return list
})

function frameImg(f) {
  return `/admin/framefile/${f.id}`
}

function holesCount(f) {
  const holes = f?.holes_metadata?.holes
  return Array.isArray(holes) ? holes.length : 0
}

async function refresh() {
  loading.value = true
  try {
    frames.value = await api.listFrames()
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

function pickFile(e) {
  const file = e.target.files?.[0]
  if (file) addFile.value = file
}

async function submitAdd() {
  if (!addForm.value.name.trim()) return message.warning('Nhập tên frame.')
  if (!addFile.value) return message.warning('Chọn file ảnh frame.')

  const fd = new FormData()
  fd.append('file', addFile.value)
  fd.append('name', addForm.value.name.trim())
  fd.append('shape', addForm.value.shape)
  if (addForm.value.holes.trim()) fd.append('holes_json', addForm.value.holes.trim())

  addBusy.value = true
  try {
    await api.createFrame(fd)
    message.success('Đã thêm frame vào kho.')
    addOpen.value = false
    addForm.value = { name: '', shape: 'square', holes: '' }
    addFile.value = null
    await refresh()
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    addBusy.value = false
  }
}

function openEdit(f) {
  editForm.value = {
    id: f.id,
    name: f.name,
    shape: f.frame,
    is_active: !!f.is_active,
    holes: f.holes_metadata ? JSON.stringify(f.holes_metadata, null, 2) : '',
  }
  editOpen.value = true
}

async function submitEdit() {
  if (!editForm.value) return
  if (!editForm.value.name.trim()) return message.warning('Nhập tên frame.')

  editBusy.value = true
  try {
    const payload = {
      name: editForm.value.name.trim(),
      shape: editForm.value.shape,
      is_active: editForm.value.is_active,
    }
    if (editForm.value.holes.trim()) {
      try { payload.holes_metadata = JSON.parse(editForm.value.holes) } catch { return message.error('Holes JSON không hợp lệ.') }
    }
    await api.updateFrame(editForm.value.id, payload)
    message.success('Đã lưu.')
    editOpen.value = false
    await refresh()
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    editBusy.value = false
  }
}

function remove(f) {
  Modal.confirm({
    title: `Xóa frame “${f.name}”?`,
    content: 'Ảnh frame local sẽ bị xóa. Booth sẽ không còn thấy frame này.',
    okText: 'Xóa',
    okType: 'danger',
    onOk: async () => {
      try {
        await api.deleteFrame(f.id)
        message.success('Đã xóa.')
        await refresh()
      } catch (e) {
        message.error(String(e.message || e))
      }
    },
  })
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Kho frame</h1>
    <p class="page-sub">Frame local — thêm/sửa/xóa. Booth đọc trực tiếp từ cache file.</p>

    <div class="toolbar">
      <a-radio-group v-model:value="shapeFilter" button-style="solid">
        <a-radio-button value="all">Tất cả ({{ frames.length }})</a-radio-button>
        <a-radio-button value="square">Vuông</a-radio-button>
        <a-radio-button value="grid">Lưới 2×2</a-radio-button>
        <a-radio-button value="strip">Strip 2×6</a-radio-button>
      </a-radio-group>
      <a-input v-model:value="search" placeholder="Tìm frame…" style="width: 220px;" allow-clear />
      <div class="spacer" />
      <a-button @click="refresh"><ReloadOutlined /> Làm mới</a-button>
      <a-button type="primary" @click="addOpen = true"><PlusOutlined /> Thêm frame</a-button>
    </div>

    <a-spin :spinning="loading">
      <div v-if="filtered.length === 0" class="empty">Chưa có frame nào. Bấm “Thêm frame” để bắt đầu.</div>
      <div v-else class="frame-grid">
        <div v-for="f in filtered" :key="f.id" class="frame-card">
          <div class="frame-thumb">
            <img :src="frameImg(f)" :alt="f.name" loading="lazy" />
          </div>
          <div class="frame-body">
            <div class="frame-name" :title="f.name">{{ f.name }}</div>
            <div class="frame-meta">{{ f.frame }} · {{ holesCount(f) }} ô ảnh · {{ f.is_active ? 'đang bật' : 'đang tắt' }}</div>
            <div class="frame-actions">
              <a-button size="small" @click="openEdit(f)">Sửa</a-button>
              <a-button size="small" danger @click="remove(f)"><DeleteOutlined /></a-button>
            </div>
          </div>
        </div>
      </div>
    </a-spin>

    <!-- Add modal -->
    <a-modal v-model:open="addOpen" title="Thêm frame mới" ok-text="Thêm" :confirm-loading="addBusy" @ok="submitAdd">
      <a-form layout="vertical">
        <a-form-item label="Tên frame" required>
          <a-input v-model:value="addForm.name" placeholder="VD: Tết 2026 hồng" />
        </a-form-item>
        <a-form-item label="Kiểu">
          <a-select v-model:value="addForm.shape">
            <a-select-option value="square">Vuông (4×6 / 6×4)</a-select-option>
            <a-select-option value="grid">Lưới 2×2</a-select-option>
            <a-select-option value="strip">Strip 2×6</a-select-option>
          </a-select>
        </a-form-item>
        <a-form-item label="File ảnh (PNG/JPG/WebP)" required>
          <input type="file" accept=".png,.jpg,.jpeg,.webp" @change="pickFile" />
          <div v-if="addFile" class="frame-meta" style="margin-top: 6px;">{{ addFile.name }} — {{ Math.round(addFile.size / 1024) }} KB</div>
        </a-form-item>
        <a-form-item label="Holes metadata (JSON, tùy chọn)">
          <a-textarea v-model:value="addForm.holes" :rows="4" placeholder='{"fw":880,"fh":2650,"holes":[{"x":56,"y":66,"w":769,"h":560}]}' />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- Edit modal -->
    <a-modal v-model:open="editOpen" title="Sửa frame" ok-text="Lưu" :confirm-loading="editBusy" @ok="submitEdit">
      <a-form v-if="editForm" layout="vertical">
        <a-form-item label="Tên frame" required>
          <a-input v-model:value="editForm.name" />
        </a-form-item>
        <a-form-item label="Kiểu">
          <a-select v-model:value="editForm.shape">
            <a-select-option value="square">Vuông</a-select-option>
            <a-select-option value="grid">Lưới 2×2</a-select-option>
            <a-select-option value="strip">Strip 2×6</a-select-option>
          </a-select>
        </a-form-item>
        <a-form-item label="Đang bật">
          <a-switch v-model:checked="editForm.is_active" />
        </a-form-item>
        <a-form-item label="Holes metadata (JSON)">
          <a-textarea v-model:value="editForm.holes" :rows="5" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>
