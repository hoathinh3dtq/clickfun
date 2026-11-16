<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'
import { message } from 'ant-design-vue'

const filters = ref([])
const sessions = ref([])
const selectedDate = ref(null)
const selectedFile = ref(null)
const photos = ref([])
const activeFilter = ref('sepia')

async function refresh() {
  try {
    filters.value = await api.filters()
    sessions.value = await api.listSessions()
    if (sessions.value.length) {
      selectedDate.value = sessions.value[0].date
      await loadPhotos()
    }
  } catch (e) {
    message.error(String(e.message || e))
  }
}

async function loadPhotos() {
  if (!selectedDate.value) return
  try {
    photos.value = await api.listSessionPhotos(selectedDate.value)
    selectedFile.value = photos.value[0]?.name || null
  } catch (e) {
    photos.value = []
  }
}

const previewUrl = (filter) => selectedFile.value && selectedDate.value
  ? api.filterPreviewUrl(selectedDate.value, selectedFile.value, filter)
  : null

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">Bá»™ lá»c</h1>
    <p class="page-sub">CÃ¡c filter cÃ³ sáºµn trong cáº§u ná»‘i áº£nh. Chá»n áº£nh Ä‘á»ƒ xem trÆ°á»›c.</p>

    <div class="card" style="margin-bottom: 16px;">
      <div class="stat-label" style="margin-bottom: 10px;">Chá»n áº£nh thá»­</div>
      <a-space wrap>
        <a-select v-model:value="selectedDate" style="width: 160px;" @change="loadPhotos">
          <a-select-option v-for="s in sessions" :key="s.date" :value="s.date">{{ s.date }} ({{ s.photos }})</a-select-option>
        </a-select>
        <a-select v-model:value="selectedFile" style="width: 260px;" placeholder="Chá»n áº£nhâ€¦">
          <a-select-option v-for="p in photos" :key="p.name" :value="p.name">{{ p.name }}</a-select-option>
        </a-select>
      </a-space>
    </div>

    <div v-if="selectedFile" class="filter-grid">
      <div v-for="f in filters" :key="f.id" class="filter-card" :class="{ active: activeFilter === f.id }" @click="activeFilter = f.id">
        <img :src="previewUrl(f.id)" :alt="f.label" />
        <div class="filter-name">{{ f.label }}</div>
      </div>
    </div>
    <div v-else class="empty">ChÆ°a cÃ³ áº£nh Ä‘á»ƒ xem trÆ°á»›c â€” chá»¥p thá»­ 1 áº£nh trÆ°á»›c.</div>
  </div>
</template>

