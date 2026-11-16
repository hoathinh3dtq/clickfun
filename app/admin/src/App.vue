<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getToken, setToken, clearToken } from './api.js'
import {
  DashboardOutlined,
  PictureOutlined,
  LayoutOutlined,
  TagsOutlined,
  SettingOutlined,
  CameraOutlined,
  FileTextOutlined,
  LockOutlined,
  LogoutOutlined,
  BarChartOutlined,
  CameraFilled,
  PrinterOutlined,
  ExperimentOutlined,
  SafetyCertificateOutlined,
} from '@ant-design/icons-vue'

const route = useRoute()
const router = useRouter()
const token = ref(getToken())
const needsToken = computed(() => !token.value)
const tokenDraft = ref('')
const error = ref('')

const menuGroups = [
  {
    label: 'Vận hành',
    items: [
      { key: 'dashboard', icon: DashboardOutlined, label: 'Tổng quan' },
      { key: 'stats', icon: BarChartOutlined, label: 'Thống kê' },
      { key: 'sessions', icon: CameraFilled, label: 'Phiên chụp' },
      { key: 'prints', icon: PrinterOutlined, label: 'Lịch sử in' },
    ],
  },
  {
    label: 'Nội dung',
    items: [
      { key: 'frames', icon: PictureOutlined, label: 'Kho frame' },
      { key: 'layouts', icon: LayoutOutlined, label: 'Layout in' },
      { key: 'categories', icon: TagsOutlined, label: 'Danh mục' },
      { key: 'filters', icon: ExperimentOutlined, label: 'Bộ lọc' },
    ],
  },
  {
    label: 'Máy & cài đặt',
    items: [
      { key: 'camera', icon: CameraOutlined, label: 'Máy ảnh & in' },
      { key: 'settings', icon: SettingOutlined, label: 'Cài đặt booth' },
      { key: 'license', icon: SafetyCertificateOutlined, label: 'Bản quyền & máy' },
      { key: 'logs', icon: FileTextOutlined, label: 'Nhật ký' },
    ],
  },
]

const selectedKeys = computed(() => [route.name || 'dashboard'])

function saveToken() {
  const t = tokenDraft.value.trim()
  if (!t) {
    error.value = 'Nhập bridge token (mặc định lúc dev: clickfun-dev-token).'
    return
  }
  setToken(t)
  token.value = t
  error.value = ''
}

function logout() {
  clearToken()
  token.value = ''
  tokenDraft.value = ''
}

function onUnauthorized() {
  token.value = ''
}

onMounted(() => window.addEventListener('clickfun-admin:unauthorized', onUnauthorized))
onUnmounted(() => window.removeEventListener('clickfun-admin:unauthorized', onUnauthorized))
</script>

<template>
  <div v-if="needsToken" class="gate">
    <div class="gate-card">
      <div class="gate-logo">CF</div>
      <h1>ClickFun Admin</h1>
      <p class="gate-sub">Khu vực quản trị local — nhập bridge token để tiếp tục.</p>
      <a-input-password
        v-model:value="tokenDraft"
        placeholder="Bridge token…"
        size="large"
        @pressEnter="saveToken"
      >
        <template #prefix><LockOutlined /></template>
      </a-input-password>
      <p v-if="error" class="gate-error">{{ error }}</p>
      <a-button type="primary" size="large" block @click="saveToken">Vào quản trị</a-button>
      <p class="gate-hint">Token lấy từ biến môi trường <code>CLICKFUN_BRIDGE_TOKEN</code> khi chạy bridge.</p>
    </div>
  </div>

  <a-layout v-else class="shell">
    <a-layout-sider width="230" class="sidebar">
      <div class="brand">
        <div class="brand-mark">CF</div>
        <div>
          <div class="brand-name">ClickFun</div>
          <div class="brand-tag">LOCAL ADMIN</div>
        </div>
      </div>
      <nav class="nav">
        <div v-for="group in menuGroups" :key="group.label" class="nav-group">
          <div class="nav-group-label">{{ group.label }}</div>
          <a-menu mode="inline" :selected-keys="selectedKeys" class="nav-menu" @click="m => router.push({ name: m.key })">
            <a-menu-item v-for="item in group.items" :key="item.key">
              <component :is="item.icon" />
              <span>{{ item.label }}</span>
            </a-menu-item>
          </a-menu>
        </div>
      </nav>
      <div class="sidebar-foot">
        <button class="logout" @click="logout"><LogoutOutlined /> Đăng xuất</button>
        <div class="foot-note">Dữ liệu lưu local<br />ProgramData\ClickFun\admin</div>
      </div>
    </a-layout-sider>

    <a-layout>
      <a-layout-header class="topbar">
        <div class="crumb">{{ route.meta?.title || 'Tổng quan' }}</div>
        <div class="topbar-right">
          <span class="dot" /> Booth local · 127.0.0.1
        </div>
      </a-layout-header>
      <a-layout-content class="content">
        <router-view />
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>
