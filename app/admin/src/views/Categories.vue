<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message, Modal } from 'ant-design-vue'
import { DeleteOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons-vue'

const categories = ref([])
const loading = ref(false)
const name = ref('')
const adding = ref(false)

async function refresh() {
  loading.value = true
  try { categories.value = await api.listCategories() } catch (e) { message.error(String(e.message || e)) } finally { loading.value = false }
}

async function add() {
  const n = name.value.trim()
  if (!n) return message.warning('Nhập tên danh mục.')
  adding.value = true
  try {
    await api.createCategory(n)
    name.value = ''
    message.success('Đã thêm.')
    await refresh()
  } catch (e) { message.error(String(e.message || e)) } finally { adding.value = false }
}

function remove(c) {
  Modal.confirm({
    title: `Xóa danh mục “${c.name}”?`,
    okText: 'Xóa',
    okType: 'danger',
    onOk: async () => {
      try { await api.deleteCategory(c.id); message.success('Đã xóa.'); await refresh() } catch (e) { message.error(String(e.message || e)) }
    },
  })
}

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Danh mục frame</h1>
    <p class="page-sub">Phân loại frame trong kho local.</p>

    <div class="toolbar">
      <a-input v-model:value="name" placeholder="Tên danh mục mới…" style="width: 280px;" @pressEnter="add" />
      <a-button type="primary" :loading="adding" @click="add"><PlusOutlined /> Thêm</a-button>
      <div class="spacer" />
      <a-button @click="refresh"><ReloadOutlined /> Làm mới</a-button>
    </div>

    <div v-if="categories.length === 0" class="empty">Chưa có danh mục nào.</div>
    <a-list v-else bordered :data-source="categories" style="background: #fff; border-radius: 12px;">
      <template #renderItem="{ item }">
        <a-list-item>
          <span><strong>{{ item.name }}</strong> <span class="frame-meta">#{{ item.id }}</span></span>
          <template #actions>
            <a-button size="small" danger @click="remove(item)"><DeleteOutlined /> Xóa</a-button>
          </template>
        </a-list-item>
      </template>
    </a-list>
  </div>
</template>
