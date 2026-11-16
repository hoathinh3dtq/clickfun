<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../api.js'
import { message, Modal } from 'ant-design-vue'
import { ReloadOutlined, DeleteOutlined, ArrowLeftOutlined } from '@ant-design/icons-vue'

const sessions = ref([])
const selected = ref(null)
const photos = ref([])
const loading = ref(false)
const loadingPhotos = ref(false)

async function refresh() {
  loading.value = true
  try {
    sessions.value = await api.listSessions()
  } catch (e) {
    message.error(String(e.message || e))
  } finally {
    loading.value = false
  }
}

async function open(date) {
  selected.value = date
  loadingPhotos.value = true
  try {
    photos.value = await api.listSessionPhotos(date)
  } catch (e) {
    message.error(String(e.message || e))
    photos.value = []
  } finally {
    loadingPhotos.value = false
  }
}

function removePhoto(p) {
  Modal.confirm({
    title: `XÃ³a áº£nh ${p.name}?`,
    content: 'áº¢nh chá»¥p sáº½ bá»‹ xÃ³a khá»i á»• Ä‘Ä©a.',
    okText: 'XÃ³a',
    okType: 'danger',
    onOk: async () => {
      try {
        await api.deletePhoto(selected.value, p.name)
        message.success('ÄÃ£ xÃ³a.')
        await open(selected.value)
      } catch (e) {
        message.error(String(e.message || e))
      }
    },
  })
}

function fmtMB(bytes) {
  return (Number(bytes || 0) / 1048576).toFixed(2) + ' MB'
}

const columns = [
  { title: 'TÃªn file', dataIndex: 'name', key: 'name' },
  { title: 'Dung lÆ°á»£ng', key: 'bytes', width: 120 },
  { title: 'Chá»¥p lÃºc', key: 'modified', width: 200 },
  { title: '', key: 'actions', width: 90 },
]

onMounted(refresh)
</script>

<template>
  <div>
    <h1 class="page-title">PhiÃªn chá»¥p</h1>
    <p class="page-sub">áº¢nh chá»¥p lÆ°u trÃªn mÃ¡y, nhÃ³m theo ngÃ y.</p>

    <div v-if="!selected">
      <div class="toolbar">
        <div class="spacer" />
        <a-button @click="refresh"><ReloadOutlined /> LÃ m má»›i</a-button>
      </div>
      <a-spin :spinning="loading">
        <div v-if="sessions.length === 0" class="empty">ChÆ°a cÃ³ áº£nh nÃ o Ä‘Æ°á»£c chá»¥p.</div>
        <div v-else class="session-grid">
          <button v-for="s in sessions" :key="s.date" class="session-card" @click="open(s.date)">
            <div class="session-date">{{ s.date }}</div>
            <div class="session-meta">{{ s.photos }} áº£nh Â· {{ fmtMB(s.bytes) }}</div>
          </button>
        </div>
      </a-spin>
    </div>

    <div v-else>
      <div class="toolbar">
        <a-button @click="selected = null"><ArrowLeftOutlined /> Quay láº¡i</a-button>
        <strong style="font-size: 16px;">{{ selected }}</strong>
        <div class="spacer" />
        <span class="frame-meta">{{ photos.length }} áº£nh</span>
      </div>
      <a-spin :spinning="loadingPhotos">
        <div v-if="photos.length === 0" class="empty">KhÃ´ng cÃ³ áº£nh trong ngÃ y nÃ y.</div>
        <div v-else class="frame-grid">
          <div v-for="p in photos" :key="p.name" class="frame-card">
            <a :href="api.photoUrl(selected, p.name)" target="_blank" class="frame-thumb" style="display:grid;place-items:center;text-decoration:none;">
              <img :src="api.photoUrl(selected, p.name)" :alt="p.name" />
            </a>
            <div class="frame-body">
              <div class="frame-name" :title="p.name">{{ p.name }}</div>
              <div class="frame-meta">{{ fmtMB(p.bytes) }}</div>
              <div class="frame-actions">
                <a-button size="small" danger @click="removePhoto(p)"><DeleteOutlined /></a-button>
              </div>
            </div>
          </div>
        </div>
      </a-spin>
    </div>
  </div>
</template>

