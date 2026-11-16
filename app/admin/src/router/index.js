import { createRouter, createWebHashHistory } from 'vue-router'
import Dashboard from '../views/Dashboard.vue'
import Frames from '../views/Frames.vue'
import Layouts from '../views/Layouts.vue'
import Categories from '../views/Categories.vue'
import Sessions from '../views/Sessions.vue'
import Prints from '../views/Prints.vue'
import Stats from '../views/Stats.vue'
import Filters from '../views/Filters.vue'
import License from '../views/License.vue'
import Settings from '../views/Settings.vue'
import Camera from '../views/Camera.vue'
import Logs from '../views/Logs.vue'

const routes = [
  { path: '/', name: 'dashboard', component: Dashboard, meta: { title: 'Tổng quan' } },
  { path: '/stats', name: 'stats', component: Stats, meta: { title: 'Thống kê' } },
  { path: '/sessions', name: 'sessions', component: Sessions, meta: { title: 'Phiên chụp' } },
  { path: '/frames', name: 'frames', component: Frames, meta: { title: 'Kho frame' } },
  { path: '/layouts', name: 'layouts', component: Layouts, meta: { title: 'Layout in' } },
  { path: '/categories', name: 'categories', component: Categories, meta: { title: 'Danh mục' } },
  { path: '/filters', name: 'filters', component: Filters, meta: { title: 'Bộ lọc' } },
  { path: '/prints', name: 'prints', component: Prints, meta: { title: 'Lịch sử in' } },
  { path: '/camera', name: 'camera', component: Camera, meta: { title: 'Máy ảnh & in' } },
  { path: '/license', name: 'license', component: License, meta: { title: 'Bản quyền & máy' } },
  { path: '/settings', name: 'settings', component: Settings, meta: { title: 'Cài đặt booth' } },
  { path: '/logs', name: 'logs', component: Logs, meta: { title: 'Nhật ký' } },
]

export default createRouter({
  history: createWebHashHistory('/admin/'),
  routes,
})
