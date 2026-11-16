<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'
import { ReloadOutlined } from '@ant-design/icons-vue'

const prints = ref([])
const sessions = ref([])
const sessionDates = ref([])
const loading = ref(false)
const filter = ref('all')

async function refresh() {
  loading.value = true
  try {
    const [pr, ses] = await Promise.all([api.prints(500), api.listSessions()])
    prints.value = Array.isArray(pr) ? pr : []
    sessions.value = Array.isArray(ses) ? ses : []
    sessionDates.value = sessions.value.map(s => s.date)
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

const filtered = computed(() => {
  if (filter.value === 'all') return prints.value
  return prints.value.filter(p => String(p.file || '').includes(filter.value))
})

function fmtTs(ts) {
  try { return new Date(ts).toLocaleString('vi-VN') } catch { return ts }
}

const columns = [
  { title: 'Thời gian', key: 'ts', width: 190 },
  { title: 'File', dataIndex: 'file', key: 'file', ellipsis: true },
  { title: 'Bản', dataIndex: 'copies', key: 'copies', width: 60 },
  { title: 'Khổ', key: 'paperLayout', width: 90 },
  { title: 'Máy in', key: 'printer', width: 140 },
  { title: 'Kết quả', key: 'success', width: 120 },
]

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Lịch sử in</h1>
    <p class="page-sub">Các lần in gần đây (tối đa 500 dòng), ghi nhận trên máy này.</p>

    <div class="toolbar">
      <a-select v-model:value="filter" style="width: 150px;">
        <a-select-option value="all">Tất cả</a-select-option>
        <a-select-option v-for="d in sessionDates" :key="d" :value="d">{{ d }}</a-select-option>
      </a-select>
      <div class="spacer" />
      <a-button @click="refresh"><ReloadOutlined /> Làm mới</a-button>
    </div>

    <a-table :data-source="filtered" :loading="loading" :pagination="{ pageSize: 15 }" row-key="ts" :columns="columns">
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'ts'">{{ fmtTs(record.ts) }}</template>
        <template v-else-if="column.key === 'file'"><span class="mono">{{ (record.file || '').split(/[\\/]/).pop() }}</span></template>
        <template v-else-if="column.key === 'paperLayout'"><a-tag>{{ record.paperLayout || 'auto' }}</a-tag></template>
        <template v-else-if="column.key === 'success'">
          <a-tag :color="record.success ? 'green' : 'red'">{{ record.success ? 'OK' : 'Lỗi' }}</a-tag>
        </template>
      </template>
    </a-table>
  </div>
</template>
