// ==================== Photino 桥 ====================
const external = window.external;

// 发消息到 C#
function send(type, payload = {}) {
    external.sendMessage(JSON.stringify({ type, payload }));
}

// 接收 C# 推送的消息
external.receiveMessage((message) => {
    try {
        const msg = JSON.parse(message);
        handleMessage(msg);
    } catch (e) {
        console.error('Parse message failed', e, message);
    }
});

// ==================== 状态 ====================
let state = {
    workshopPath: '',
    isScanning: false,
    scanStatusText: '',
    integrityFilter: 'All',
    typeFilter: 'All',
    sortBy: 'Title',
    sortDescending: false,
    showDeprecatedOnly: false,
    searchText: '',
    totalCount: 0,
    selectedCount: 0,
    selectedTotalSize: '—',
    filteredCount: 0,
    isCalculatingAllSizes: false,
    items: [],
    selectedItem: null
};
// 缩略图缓存：workshopId → data URI
const thumbCache = {};
// 预览图缓存：workshopId → data URI
const previewCache = {};

// ==================== 消息分发 ====================
function handleMessage(msg) {
    switch (msg.type) {
        case 'state':
            applyState(msg.payload);
            break;
        case 'thumb':
            if (msg.data) {
                thumbCache[msg.workshopId] = msg.data;
                updateThumbInList(msg.workshopId, msg.data);
            }
            break;
        case 'preview':
            if (msg.data) {
                previewCache[msg.workshopId] = msg.data;
                updatePreviewImage(msg.workshopId, msg.data);
            }
            break;
    }
}

// ==================== 状态应用（增量更新 DOM） ====================
function applyState(newState) {
    const oldSelectedId = state.selectedItem?.workshopId;
    state = Object.assign({}, state, newState);

    // 顶部工具栏
    document.getElementById('path-display').textContent = state.workshopPath || '未选择目录';
    document.getElementById('btn-refresh').disabled = state.isScanning || !state.workshopPath;
    document.getElementById('btn-calc-all').disabled = state.isCalculatingAllSizes || state.isScanning;
    document.getElementById('btn-cancel-calc').classList.toggle('hidden', !state.isCalculatingAllSizes);

    // 筛选栏同步（避免回环：只在值不同时更新）
    syncFilterControls();

    // 底部状态栏
    const stats = `共 ${state.totalCount} 项 | 完整 ${state.completeCount || 0} | 缺资源 ${state.missingResourceCount || 0} | 损坏 ${state.corruptCount || 0} | 非壁纸 ${state.nonWallpaperCount || 0}`;
    document.getElementById('status-stats').textContent = stats;
    document.getElementById('status-scan').textContent = state.scanStatusText || '';
    document.getElementById('status-selected').textContent = state.selectedCount > 0
        ? `已选 ${state.selectedCount} 项，合计 ${state.selectedTotalSize}` : '';

    // 左栏：列表（每次重渲染，量小可接受）
    renderList();

    // 中栏 + 右栏：选中项
    const newSelectedId = state.selectedItem?.workshopId;
    if (newSelectedId !== oldSelectedId) {
        renderCenter();
        renderRight();
        // 异步请求预览图
        if (newSelectedId && state.selectedItem?.hasPreview && !previewCache[newSelectedId]) {
            send('request-preview', { workshopId: newSelectedId });
        }
    } else if (state.selectedItem) {
        // 选中项内容更新（如大小状态变化）
        renderCenter();
        renderRight();
    }
}

function syncFilterControls() {
    const set = (id, value) => {
        const el = document.getElementById(id);
        if (el && el.value !== value) el.value = value;
    };
    const setCheck = (id, value) => {
        const el = document.getElementById(id);
        if (el && el.checked !== value) el.checked = value;
    };
    set('filter-integrity', state.integrityFilter);
    set('filter-type', state.typeFilter);
    set('filter-sort', state.sortBy);
    setCheck('filter-desc', state.sortDescending);
    setCheck('filter-deprecated', state.showDeprecatedOnly);
    set('filter-search', state.searchText);
}

// ==================== 左栏列表渲染 ====================
function renderList() {
    const container = document.getElementById('list-container');
    if (!state.items || state.items.length === 0) {
        container.innerHTML = state.workshopPath
            ? '<div class="placeholder">该目录下没有壁纸项目</div>'
            : '<div class="placeholder">请先选择 Workshop 目录</div>';
        return;
    }

    const items = state.items;
    const selectedId = state.selectedItem?.workshopId;

    // 复用已有 DOM 节点：按 data-id 索引旧节点
    const existingNodes = new Map();
    container.querySelectorAll('.list-item').forEach(el => {
        existingNodes.set(el.dataset.id, el);
    });

    // 第一遍：更新/创建节点，但不替换 innerHTML
    const fragment = document.createDocumentFragment();
    items.forEach(item => {
        const id = item.workshopId;
        let el = existingNodes.get(id);

        if (!el) {
            // 新建节点
            el = document.createElement('div');
            el.className = 'list-item';
            el.dataset.id = id;
            el.innerHTML = `
                <input type="checkbox" class="checkbox">
                <div class="thumb"></div>
                <div class="info">
                    <div class="title"></div>
                    <div class="sub"></div>
                </div>
            `;
            attachListItemEvents(el);
        } else {
            existingNodes.delete(id);  // 标记为已复用
        }

        // 增量更新：选中态
        const isSel = id === selectedId;
        el.classList.toggle('selected', isSel);

        // 增量更新：勾选态（避免无谓赋值触发副作用）
        const cb = el.querySelector('.checkbox');
        if (cb.checked !== item.isSelected) cb.checked = item.isSelected;

        // 标题/副标题（内容可能变化）
        el.querySelector('.title').textContent = item.title;
        el.querySelector('.sub').innerHTML =
            `${item.integrityIcon} ${escapeHtml(item.typeDisplay)} · ${escapeHtml(item.integrityDisplay)} · ${escapeHtml(item.sizeDisplay)}${item.isDeprecated ? ' · <span class="badge">废弃</span>' : ''}`;

        // 缩略图：有缓存就填充，无则保持占位并订阅懒加载
        const thumbEl = el.querySelector('.thumb');
        if (thumbCache[id]) {
            if (thumbEl.tagName === 'DIV' || thumbEl.getAttribute('src') !== thumbCache[id]) {
                const img = document.createElement('img');
                img.className = 'thumb';
                img.src = thumbCache[id];
                thumbEl.replaceWith(img);
            }
        } else if (thumbEl.tagName === 'DIV') {
            thumbObserver.observe(thumbEl);
        }

        fragment.appendChild(el);
    });

    // 剩余在 existingNodes 中的是已被删除的项，丢弃即可
    // 用 fragment 替换容器内容（一次性 reflow）
    container.replaceChildren(fragment);
}

// 列表项事件绑定（节点复用模式下只绑定一次）
function attachListItemEvents(el) {
    const id = el.dataset.id;

    // 单击项（非 checkbox）→ 选中查看
    el.addEventListener('click', (e) => {
        if (e.target.classList.contains('checkbox')) return;
        send('select-item', { workshopId: id });
    });

    // 双击项（非 checkbox）→ 切换勾选状态（不做即时反馈，由 C# state 回程驱动，避免闪烁）
    el.addEventListener('dblclick', (e) => {
        if (e.target.classList.contains('checkbox')) return;
        const cb = el.querySelector('.checkbox');
        send('toggle-select', { workshopId: id, selected: !cb.checked });
    });

    // checkbox：原生 toggle 作为即时反馈，change 时统一发送；
    // 关键：在 send 前后冻结输入 ~300ms，吸收双击的第二次 toggle，避免视觉闪烁
    const cb = el.querySelector('.checkbox');
    let toggling = false;
    cb.addEventListener('click', (e) => {
        if (toggling) { e.preventDefault(); return; }  // 冻结期内，撤销本次 toggle
        e.stopPropagation();
    });
    cb.addEventListener('change', () => {
        if (toggling) return;
        toggling = true;
        send('toggle-select', { workshopId: id, selected: cb.checked });
        // 冻结 300ms 吸收双击的第二次 click/change
        setTimeout(() => { toggling = false; }, 300);
    });
}

// IntersectionObserver：列表项缩略图占位进入视口时触发请求
const thumbObserver = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        const div = entry.target;
        const id = div.closest('.list-item')?.dataset.id;
        thumbObserver.unobserve(div);
        if (id) send('request-thumb', { workshopId: id });
    });
}, { root: document.getElementById('list-container'), rootMargin: '100px' });

function updateThumbInList(workshopId, dataUri) {
    const item = document.querySelector(`.list-item[data-id="${workshopId}"] .thumb`);
    if (item) {
        const img = document.createElement('img');
        img.className = 'thumb';
        img.src = dataUri;
        item.replaceWith(img);
    }
}

// ==================== 中栏 ====================
function renderCenter() {
    const body = document.getElementById('center-body');
    const sel = state.selectedItem;
    if (!sel) {
        body.innerHTML = '<div class="placeholder">从左侧选择一项</div>';
        return;
    }

    let previewHtml;
    if (previewCache[sel.workshopId]) {
        previewHtml = `<img class="preview-img" src="${previewCache[sel.workshopId]}">`;
    } else if (sel.hasPreview) {
        previewHtml = '<div class="preview-empty">加载预览中…</div>';
    } else {
        previewHtml = '<div class="preview-empty">（无预览图）</div>';
    }

    body.innerHTML = `
        <div class="preview-section">${previewHtml}</div>
        <div class="center-title">${escapeHtml(sel.title)}${sel.isDeprecated ? ' <span class="badge">废弃</span>' : ''}</div>
        ${sel.description ? `<div class="center-desc">${escapeHtml(sel.description)}</div>` : ''}
        ${sel.tagsDisplay && sel.tagsDisplay !== '—' ? `<div class="center-tags"><span class="tag-label">标签：</span>${escapeHtml(sel.tagsDisplay)}</div>` : ''}
    `;
}

function updatePreviewImage(workshopId, dataUri) {
    if (state.selectedItem?.workshopId !== workshopId) return;
    const img = document.querySelector('#center-body .preview-img');
    if (img) img.src = dataUri;
    else renderCenter();
}

// ==================== 右栏 ====================
function renderRight() {
    const body = document.getElementById('right-body');
    const sel = state.selectedItem;
    if (!sel) {
        body.innerHTML = '<div class="placeholder">从左侧选择一项</div>';
        return;
    }

    const rows = [
        ['Workshop ID', sel.workshopIdDeclared || sel.workshopId],
        ['类型', sel.typeDisplay],
        ['可见性', sel.visibility || '—'],
        ['完整性', `${sel.integrityIcon} ${sel.integrityDisplay}`],
    ];

    let sizeValue = escapeHtml(sel.sizeDisplay);
    if (sel.isSizeUnknown) {
        sizeValue += ` <button id="btn-calc-one" style="padding:1px 8px;font-size:11px;margin-left:4px;">计算大小</button>`;
    }
    rows.push(['占用大小', sizeValue]);

    rows.push(['最后修改', sel.directoryLastWrite]);
    if (sel.hasFileRelative) rows.push(['主文件', escapeHtml(sel.fileRelative)]);

    // 详情：放在 detail-section（可滚动）
    const detailHtml = `
        <div class="detail-section">
            <div class="detail-card">
                ${rows.map(([k, v]) => `<div class="detail-row"><span class="label">${k}</span><span class="value">${v}</span></div>`).join('')}
            </div>
        </div>
    `;

    // 操作：放在 actions-section（置底，不可滚动）
    const actionsHtml = `
        <div class="actions-section">
            <div class="action-buttons">
                <div class="row">
                    <button id="btn-open-dir">打开目录</button>
                    <button id="btn-open-ws">Workshop</button>
                </div>
                <div class="row">
                    <button id="btn-toggle-deprecated">${escapeHtml(sel.toggleDeprecatedText)}</button>
                    <button id="btn-recycle-current" class="danger">删除</button>
                </div>
                <button id="btn-delete-current" class="danger">永久删除</button>
                <hr class="action-divider">
                <div class="row">
                    <button id="btn-toggle-deprecated-selected">废弃选中</button>
                    <button id="btn-recycle-selected" class="danger">删除选中</button>
                </div>
                <button id="btn-delete-selected" class="danger">永久删除选中</button>
            </div>
        </div>
    `;

    body.innerHTML = detailHtml + actionsHtml;

    document.getElementById('btn-open-dir')?.addEventListener('click', () => send('open-directory', { workshopId: sel.workshopId }));
    document.getElementById('btn-open-ws')?.addEventListener('click', () => send('open-workshop', { workshopId: sel.workshopId }));
    document.getElementById('btn-toggle-deprecated')?.addEventListener('click', () => send('toggle-deprecated', { workshopId: sel.workshopId }));
    document.getElementById('btn-calc-one')?.addEventListener('click', () => send('calc-one-size', { workshopId: sel.workshopId }));
    document.getElementById('btn-recycle-current')?.addEventListener('click', () => send('delete-current', { permanent: false }));
    document.getElementById('btn-delete-current')?.addEventListener('click', () => send('delete-current', { permanent: true }));
    document.getElementById('btn-toggle-deprecated-selected')?.addEventListener('click', () => send('toggle-deprecated-selected'));
    document.getElementById('btn-recycle-selected')?.addEventListener('click', () => send('delete-selected', { permanent: false }));
    document.getElementById('btn-delete-selected')?.addEventListener('click', () => send('delete-selected', { permanent: true }));
}

// ==================== 工具栏 + 筛选栏事件 ====================
document.getElementById('btn-select-dir').addEventListener('click', () => send('select-dir'));
document.getElementById('btn-refresh').addEventListener('click', () => send('refresh'));
document.getElementById('btn-calc-all').addEventListener('click', () => send('calc-all-sizes'));
document.getElementById('btn-cancel-calc').addEventListener('click', () => send('cancel-size-calc'));
document.getElementById('btn-select-all').addEventListener('click', () => send('select-all'));
document.getElementById('btn-invert').addEventListener('click', () => send('invert-select'));
document.getElementById('btn-clear').addEventListener('click', () => send('clear-select'));

// 筛选/排序/搜索：变化时整体推送
let filterDebounce = null;
function pushFilter() {
    if (filterDebounce) clearTimeout(filterDebounce);
    filterDebounce = setTimeout(() => {
        send('set-filter', {
            integrity: document.getElementById('filter-integrity').value,
            type: document.getElementById('filter-type').value,
            sortBy: document.getElementById('filter-sort').value,
            descending: document.getElementById('filter-desc').checked,
            deprecatedOnly: document.getElementById('filter-deprecated').checked,
            search: document.getElementById('filter-search').value
        });
    }, 200);
}
['filter-integrity', 'filter-type', 'filter-sort', 'filter-desc', 'filter-deprecated'].forEach(id => {
    document.getElementById(id).addEventListener('change', pushFilter);
});
document.getElementById('filter-search').addEventListener('input', pushFilter);

// ==================== 列宽约束 ====================
// 三栏布局：左 col-left（width）、中 col-center（flex:1 吸收剩余）、右 col-right（width）
// 左右栏是 flex:0 0 auto（不伸缩），中栏 flex:1 吸收剩余。
// 当窗口缩小时，需主动钳制左右栏宽度，避免 left+right+center(min) 溢出 .main。
const COL_CONSTRAINTS = {
    MIN_LEFT: 280,
    MIN_CENTER: 360,
    MIN_RIGHT: 280,
    SPLITTERS_TOTAL: 8,  // 两个 splitter 各 4px
};
function clampColumns() {
    const leftCol = document.getElementById('col-left');
    const rightCol = document.getElementById('col-right');
    const main = leftCol.parentElement;  // .main
    if (!leftCol || !rightCol || !main) return;

    const mainWidth = main.clientWidth;
    const { MIN_LEFT, MIN_CENTER, MIN_RIGHT, SPLITTERS_TOTAL } = COL_CONSTRAINTS;
    // 两栏可占用的最大总宽 = mainWidth - 中栏最小 - splitter
    const maxSideTotal = mainWidth - MIN_CENTER - SPLITTERS_TOTAL;

    let leftW = leftCol.offsetWidth;
    let rightW = rightCol.offsetWidth;

    // 若总宽超出，按比例收缩两栏（保底各自 MIN）
    const sideTotal = leftW + rightW;
    if (sideTotal > maxSideTotal) {
        const ratio = maxSideTotal / sideTotal;
        leftW = Math.max(MIN_LEFT, Math.round(leftW * ratio));
        rightW = Math.max(MIN_RIGHT, Math.round(rightW * ratio));
        // 比例收缩后仍可能溢出（因各自触底），再次按"砍较大的那一栏"收敛
        let guard = 0;
        while (leftW + rightW > maxSideTotal && guard++ < 10) {
            if (leftW > MIN_LEFT) leftW--;
            else if (rightW > MIN_RIGHT) rightW--;
            else break;
        }
    }
    leftCol.style.width = leftW + 'px';
    rightCol.style.width = rightW + 'px';
}
window.addEventListener('resize', clampColumns);

// ==================== 分割条拖拽 ====================
// 左调整条：改变 col-left 宽度；右调整条：改变 col-right 宽度。
// 中栏自动吸收/释放剩余空间，但需保证三栏各自的 min-width 不被突破。
document.querySelectorAll('.splitter').forEach(splitter => {
    splitter.addEventListener('mousedown', (e) => {
        e.preventDefault();
        const target = splitter.dataset.target;  // 'left' | 'right'
        const leftCol = document.getElementById('col-left');
        const centerCol = document.getElementById('col-center');
        const rightCol = document.getElementById('col-right');

        const { MIN_LEFT, MIN_CENTER, MIN_RIGHT, SPLITTERS_TOTAL } = COL_CONSTRAINTS;

        const startX = e.clientX;
        const startLeftW = leftCol.offsetWidth;
        const startRightW = rightCol.offsetWidth;
        const mainWidth = leftCol.parentElement.offsetWidth;  // .main 总宽（近似常量）

        const onMove = (ev) => {
            const delta = ev.clientX - startX;

            if (target === 'left') {
                // 左栏宽度 = startLeft + delta，但需保证：
                //   1) >= MIN_LEFT
                //   2) 中栏剩余空间 >= MIN_CENTER → left <= mainWidth - startRight - MIN_CENTER - splitters
                //   3) 右栏不被压缩（已 flex:0 0 auto，宽度固定）
                const maxByCenter = mainWidth - startRightW - MIN_CENTER - SPLITTERS_TOTAL;
                const newLeft = Math.min(Math.max(MIN_LEFT, startLeftW + delta), maxByCenter);
                leftCol.style.width = newLeft + 'px';
            } else if (target === 'right') {
                // 右栏宽度 = startRight - delta（鼠标向左拖 delta<0 → 右栏变宽）
                // 约束：>= MIN_RIGHT 且 中栏剩余 >= MIN_CENTER
                const maxByCenter = mainWidth - startLeftW - MIN_CENTER - SPLITTERS_TOTAL;
                const newRight = Math.min(Math.max(MIN_RIGHT, startRightW - delta), maxByCenter);
                rightCol.style.width = newRight + 'px';
            }
        };
        const onUp = () => {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            document.body.style.cursor = '';
            splitter.classList.remove('dragging');
            clampColumns();  // 收尾时统一钳制一次，与 resize 逻辑一致
        };
        document.body.style.cursor = 'col-resize';
        splitter.classList.add('dragging');
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
});

// ==================== 工具函数 ====================
function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

console.log('app.js loaded');

// 初始钳制一次列宽（应对窗口初始尺寸小于默认列宽之和的情况）
window.addEventListener('load', clampColumns);
clampColumns();
