import { createRouter, createWebHashHistory } from 'vue-router'
import Dashboard from '../views/Dashboard.vue'
import Frames from '../views/Frames.vue'
import Layouts from '../views/Layouts.vue'
import Categories from '../views/Categories.vue'
import Settings from '../views/Settings.vue'
import Camera from '../views/Camera.vue'
import Logs from '../views/Logs.vue'

const routes = [
  { path: '/', name: 'dashboard', component: Dashboard, meta: { title: 'Tổng quan' } },
  { path: '/frames', name: 'frames', component: Frames, meta: { title: 'Kho frame' } },
  { path: '/layouts', name: 'layouts', component: Layouts, meta: { title: 'Layout in' } },
  { path: '/categories', name: 'categories', component: Categories, meta: { title: 'Danh mục' } },
  { path: '/settings', name: 'settings', component: Settings, meta: { title: 'Cài đặt booth' } },
  { path: '/camera', name: 'camera', component: Camera, meta: { title: 'Máy ảnh & in' } },
  { path: '/logs', name: 'logs', component: Logs, meta: { title: 'Nhật ký' } },
]

export default createRouter({
  history: createWebHashHistory('/admin/'),
  routes,
})
