const canvas = document.querySelector('#scene');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x050709);
const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 1000);
const controls = new THREE.OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;

const metaList = document.querySelector('#meta');
const jsonInput = document.querySelector('#jsonInput');
const legendInput = document.querySelector('#legendInput');
const stackInput = document.querySelector('#stackInput');
const shareBtn = document.querySelector('#shareBtn');
const exportBtn = document.querySelector('#exportBtn');
const voteBtn = document.querySelector('#voteBtn');
const voteCount = document.querySelector('#voteCount');

let currentMeta = null;
let currentLegend = null;
let currentBlueprint = null;
let currentMesh = null;
let currentSlug = null;

function resizeRenderer() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

window.addEventListener('resize', resizeRenderer);
resizeRenderer();

camera.position.set(20, 20, 20);
controls.target.set(0, 0, 0);

const hemi = new THREE.HemisphereLight(0xffffff, 0x111111, 0.6);
scene.add(hemi);
const dir = new THREE.DirectionalLight(0xffffff, 0.6);
dir.position.set(30, 50, 25);
scene.add(dir);

(function animate() {
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
})();

jsonInput.addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const text = await file.text();
  currentMeta = JSON.parse(text);
  currentBlueprint = null;
  if (stackInput) stackInput.value = '';
  updateMetaPanel();
  updateShareState();
});

legendInput.addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const dataUrl = await readAsDataURL(file);
  const image = await loadImage(dataUrl);
  const { pixels, width, height } = readPixels(image);
  currentLegend = { pixels, width, height };
  buildLegendMesh();
  updateShareState();
});

stackInput.addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (!file) return;
  try {
    const text = await file.text();
    currentBlueprint = JSON.parse(text);
  } catch (err) {
    console.warn('Failed to parse stack JSON', err);
    currentBlueprint = null;
  }
  updateMetaPanel();
  updateShareState();
});

shareBtn.addEventListener('click', () => {
  if (!currentSlug) return;
  const payload = {
    meta: currentMeta,
    legend: serializeLegend(currentLegend),
    blueprint: currentBlueprint,
  };
  localStorage.setItem(`map-preview:${currentSlug}`, JSON.stringify(payload));
  const url = new URL(window.location.href);
  url.searchParams.set('map', currentSlug);
  navigator.clipboard.writeText(url.toString());
  shareBtn.textContent = 'Link Copied!';
  setTimeout(() => (shareBtn.textContent = 'Copy Share Link'), 2000);
});

exportBtn.addEventListener('click', async () => {
  if (exportBtn.disabled || !currentMeta || !currentLegend) return;
  exportBtn.disabled = true;
  try {
    await exportUnityPackage();
  } catch (err) {
    console.warn('Failed to export package', err);
  } finally {
    exportBtn.disabled = false;
  }
});

voteBtn.addEventListener('click', () => {
  if (!currentSlug) return;
  const key = `map-votes:${currentSlug}`;
  const votes = Number(localStorage.getItem(key) || '0') + 1;
  localStorage.setItem(key, String(votes));
  voteCount.textContent = `${votes} votes`;
});

function updateMetaPanel() {
  metaList.innerHTML = '';
  if (!currentMeta) return;
  const qc = currentMeta.qc ?? {};
  const entries = {
    Name: currentMeta.map_name,
    Size: currentMeta.size_px ? `${currentMeta.size_px[0]}×${currentMeta.size_px[1]} px` : 'n/a',
    Cell: `${currentMeta.cell_size_m ?? '?'} m`,
    Area: qc.map_area_m2 ? `${qc.map_area_m2.toFixed(1)} m²` : 'n/a',
    Spawns: qc.num_spawns ?? 'n/a',
    'Spawn Balance': qc.spawn_balance != null ? `${(qc.spawn_balance * 100).toFixed(1)}%` : 'n/a',
    'Navmesh Coverage': qc.navmesh_coverage_pct != null ? `${qc.navmesh_coverage_pct.toFixed(1)}%` : 'n/a',
    'Sight Lines': qc.avg_long_los_m != null ? `${qc.avg_long_los_m.toFixed(1)}m avg / ${(qc.longest_los_m ?? 0).toFixed(1)}m max` : 'n/a',
    'Triangles': qc.triangle_count != null ? `${Math.round(qc.triangle_count).toLocaleString()}` : 'n/a',
    'Draw Calls': qc.draw_calls ?? 'n/a',
  };
  if (currentBlueprint) {
    entries['Stack'] = currentBlueprint.name || currentBlueprint.sourceName || 'Loaded blueprint';
  }
  Object.entries(entries).forEach(([key, value]) => {
    const dt = document.createElement('dt');
    dt.textContent = key;
    const dd = document.createElement('dd');
    dd.textContent = value ?? 'n/a';
    metaList.appendChild(dt);
    metaList.appendChild(dd);
  });
}

function buildLegendMesh() {
  if (!currentLegend) return;
  if (currentMesh) {
    scene.remove(currentMesh);
    currentMesh.geometry.dispose();
    currentMesh.material.dispose();
  }
  const { width, height } = currentLegend;
  const pixelArray = currentLegend.pixels instanceof Uint8ClampedArray
    ? currentLegend.pixels
    : Uint8ClampedArray.from(currentLegend.pixels);
  currentLegend.pixels = pixelArray;
  const geometry = new THREE.PlaneGeometry(width, height, width - 1, height - 1);
  const colors = [];
  for (let i = 0; i < pixelArray.length; i += 4) {
    colors.push(pixelArray[i] / 255, pixelArray[i + 1] / 255, pixelArray[i + 2] / 255);
  }
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  geometry.rotateX(-Math.PI / 2);
  const material = new THREE.MeshStandardMaterial({ vertexColors: true, side: THREE.DoubleSide });
  currentMesh = new THREE.Mesh(geometry, material);
  scene.add(currentMesh);
  controls.target.set(0, 0, 0);
  camera.position.set(0, Math.max(width, height), Math.max(width, height));
}

function updateShareState() {
  if (!currentMeta || !currentLegend) {
    shareBtn.disabled = true;
    exportBtn.disabled = true;
    voteBtn.disabled = true;
    voteCount.textContent = '';
    currentSlug = null;
    return;
  }
  currentSlug = slugify(currentMeta.map_name || `map_${Date.now()}`);
  shareBtn.disabled = false;
  exportBtn.disabled = false;
  voteBtn.disabled = false;
  const key = `map-votes:${currentSlug}`;
  const votes = localStorage.getItem(key) || 0;
  voteCount.textContent = `${votes} votes`;
}

function slugify(text) {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'map';
}

function readAsDataURL(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = reject;
    img.src = src;
  });
}

function readPixels(image) {
  const offscreen = document.createElement('canvas');
  offscreen.width = image.width;
  offscreen.height = image.height;
  const ctx = offscreen.getContext('2d');
  ctx.drawImage(image, 0, 0);
  const data = ctx.getImageData(0, 0, image.width, image.height);
  return { pixels: data.data, width: image.width, height: image.height };
}

function serializeLegend(legend) {
  if (!legend) return null;
  return {
    width: legend.width,
    height: legend.height,
    pixels: Array.from(legend.pixels),
  };
}

function deserializeLegend(payload) {
  if (!payload) return null;
  return {
    width: payload.width,
    height: payload.height,
    pixels: Uint8ClampedArray.from(payload.pixels || []),
  };
}

async function exportUnityPackage() {
  if (!window.JSZip) {
    throw new Error('JSZip not loaded');
  }
  const zip = new JSZip();
  zip.file('map.json', JSON.stringify(currentMeta, null, 2));
  if (currentBlueprint) {
    zip.file('stack.json', JSON.stringify(currentBlueprint, null, 2));
  }
  const legendBlob = await legendToBlob();
  zip.file('legend.png', legendBlob);
  const blob = await zip.generateAsync({ type: 'blob' });
  const name = `${currentSlug || currentMeta.map_name || 'map'}_unity.zip`;
  downloadBlob(blob, name);
}

function legendToBlob() {
  return new Promise((resolve, reject) => {
    if (!currentLegend) {
      reject(new Error('Missing legend'));
      return;
    }
    const { width, height } = currentLegend;
    const pixels = currentLegend.pixels instanceof Uint8ClampedArray
      ? currentLegend.pixels
      : Uint8ClampedArray.from(currentLegend.pixels || []);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    const imageData = new ImageData(new Uint8ClampedArray(pixels), width, height);
    ctx.putImageData(imageData, 0, 0);
    canvas.toBlob((blob) => {
      if (!blob) {
        reject(new Error('Failed to encode legend'));
        return;
      }
      resolve(blob);
    }, 'image/png');
  });
}

function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

(function hydrateFromQuery() {
  const params = new URLSearchParams(window.location.search);
  const slug = params.get('map');
  if (!slug) return;
  const payload = localStorage.getItem(`map-preview:${slug}`);
  if (!payload) return;
  try {
    const data = JSON.parse(payload);
    currentSlug = slug;
    currentMeta = data.meta;
    currentLegend = deserializeLegend(data.legend);
    currentBlueprint = data.blueprint || null;
    updateMetaPanel();
    buildLegendMesh();
    shareBtn.disabled = false;
    exportBtn.disabled = !currentLegend || !currentMeta;
    voteBtn.disabled = false;
    const votes = localStorage.getItem(`map-votes:${slug}`) || 0;
    voteCount.textContent = `${votes} votes`;
    updateShareState();
  } catch (err) {
    console.warn('Failed to hydrate map', err);
  }
})();
