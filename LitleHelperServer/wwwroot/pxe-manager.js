// =============================================================================
// PixelHelper - PXE Server Management Module (pxe-manager.js)
// =============================================================================

let pxeCurrentTab = 'clients';
let pxeCurrentConfig = 'grub';
let pxeCurrentLog = 'dnsmasq';
let pxeStatusCache = null;
let pxeLogInterval = null;

async function renderPxePage() {
    const content = $('#content');
    content.innerHTML = '<div class="empty">Проверка подключения к PXE серверу…</div>';

    try {
        const settings = await api('/pxe/settings');
        // Test connection
        const testRes = await api('/pxe/test-connection', 'POST', { serverUrl: settings.serverUrl });

        if (!settings.enabled || !testRes.success) {
            renderPxeConnectionScreen(settings, testRes);
        } else {
            renderPxeDashboard(settings, testRes.data);
        }
    } catch (err) {
        content.innerHTML = `
            <div class="panel" style="max-width:700px;margin:30px auto;padding:24px;">
                <div class="panel-head"><h2>Ошибка доступа к модулю PXE</h2></div>
                <div style="padding:20px;color:var(--muted);font-size:13px;">
                    <p style="color:#b62e46;margin-bottom:12px;"><strong>${escape(err.message || 'Ошибка сети')}</strong></p>
                    <p>Убедитесь, что у вашей учетной записи есть право <code>deployment.manage</code> и сервер помощника запущен.</p>
                </div>
            </div>`;
    }
}

// Explicit global attachment
window.renderPxePage = renderPxePage;

// -----------------------------------------------------------------------------
// Screen 1: Connection Settings
// -----------------------------------------------------------------------------
function renderPxeConnectionScreen(settings, testRes) {
    const isError = testRes && !testRes.success;
    const content = $('#content');
    content.innerHTML = `
        <div class="settings-card" style="max-width:720px;margin:20px auto;">
            <div style="display:flex;align-items:center;gap:14px;margin-bottom:20px;">
                <div style="width:44px;height:44px;border-radius:12px;background:#246bfd15;color:#246bfd;display:grid;place-items:center;font-size:22px;">⚡</div>
                <div>
                    <h2 style="margin:0;font-size:18px;">Подключение к PXE-серверу</h2>
                    <p style="margin:4px 0 0;color:var(--muted);font-size:13px;">Сетевая установка ОС, хранилище дистрибутивов и управление загрузчиками</p>
                </div>
            </div>

            ${isError ? `
                <div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:12px 16px;border-radius:8px;font-size:13px;margin-bottom:20px;">
                    <strong>Связь с сервером не установлена:</strong> ${escape(testRes.message || 'Сервер недоступен')}
                    <div style="font-size:11px;margin-top:4px;color:#b91c1c;">Проверьте, запущен ли контейнер PXE (порт 8080) и доступен ли он с этого хоста.</div>
                </div>` : ''}

            <form id="pxe-connect-form">
                <label>
                    <span>Адрес и порт PXE-сервера (URL)</span>
                    <input name="serverUrl" value="${escape(settings.serverUrl || 'http://172.16.16.6:8080')}" placeholder="http://172.16.16.6:8080" required>
                    <small>Адрес FastAPI-панели управления PXE (по умолчанию порт 8080 на выделенном сервере или локально).</small>
                </label>

                <div class="form-row">
                    <label>
                        <span>Таймаут подключения (сек)</span>
                        <input name="timeoutSeconds" type="number" min="3" max="60" value="${settings.timeoutSeconds || 10}">
                    </label>
                    <label>
                        <span>API-ключ (опционально)</span>
                        <input name="apiKey" type="password" value="${escape(settings.apiKey || '')}" placeholder="Секретный токен, если включен">
                    </label>
                </div>

                <div style="margin-top:10px;display:flex;align-items:center;gap:12px;">
                    <button type="submit" class="primary" id="btn-save-pxe">
                        Подключить и перейти к управлению →
                    </button>
                    <button type="button" class="secondary" id="btn-test-pxe">
                        Тест соединения
                    </button>
                </div>
            </form>
        </div>
    `;

    $('#btn-test-pxe').addEventListener('click', async () => {
        const url = $('#pxe-connect-form [name="serverUrl"]').value.trim();
        toast('Проверка соединения…');
        try {
            const res = await api('/pxe/test-connection', 'POST', { serverUrl: url });
            if (res.success) {
                toast(`Успешно! Ответ за ${res.latencyMs || 0} мс.`);
            } else {
                toast(res.message || 'Сервер не отвечает', true);
            }
        } catch (e) {
            toast(e.message, true);
        }
    });

    $('#pxe-connect-form').addEventListener('submit', async (e) => {
        e.preventDefault();
        const f = e.target;
        const btn = $('#btn-save-pxe');
        btn.disabled = true;
        btn.textContent = 'Подключение…';

        try {
            const payload = {
                enabled: true,
                serverUrl: f.serverUrl.value.trim(),
                timeoutSeconds: parseInt(f.timeoutSeconds.value, 10) || 10,
                apiKey: f.apiKey.value.trim()
            };

            await api('/pxe/settings', 'PUT', payload);

            toast('Настройки PXE сохранены');
            await renderPxePage();
        } catch (err) {
            toast(err.message, true);
        } finally {
            btn.disabled = false;
            btn.textContent = 'Подключить и перейти к управлению →';
        }
    });
}

// -----------------------------------------------------------------------------
// Screen 2: Main Dashboard & Operations
// -----------------------------------------------------------------------------
function renderPxeDashboard(settings, initialStatus) {
    pxeStatusCache = initialStatus;
    const stats = initialStatus?.stats || {};
    const services = initialStatus?.services || [];
    const clients = initialStatus?.clients || [];

    const content = $('#content');
    content.innerHTML = `
        <!-- Top Status Bar & Quick Controls -->
        <div style="background:#fff;border:1px solid var(--line);border-radius:12px;padding:16px 22px;margin-bottom:20px;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;">
            <div style="display:flex;align-items:center;gap:12px;">
                <span class="status" style="background:#edf8f3;color:#208864;font-weight:600;">
                    <span style="width:8px;height:8px;border-radius:50%;background:#208864;display:inline-block;" class="pulse"></span>
                    Подключено: ${escape(settings.serverUrl)}
                </span>
                <span style="font-size:12px;color:var(--muted);">Сервер PXE в сети · Multi-Arch Boot Engine</span>
            </div>
            <div style="display:flex;gap:8px;">
                <button class="secondary" id="pxe-btn-refresh" style="font-size:12px;padding:7px 12px;">↻ Обновить</button>
                <button class="quiet" id="pxe-btn-settings" style="font-size:12px;padding:7px 10px;">⚙ Настройки подключения</button>
            </div>
        </div>

        <!-- Metrics Row -->
        <div class="stats" style="margin-bottom:20px;">
            <div class="stat">
                <span>НАГРУЗКА CPU</span>
                <strong class="blue" id="pxe-stat-cpu">${stats.cpu_percent != null ? stats.cpu_percent + '%' : '--'}</strong>
            </div>
            <div class="stat">
                <span>ИСПОЛЬЗОВАНИЕ RAM</span>
                <strong id="pxe-stat-ram">${stats.memory_used_mb || 0} / ${stats.memory_total_mb || 512} MB</strong>
            </div>
            <div class="stat">
                <span>ХРАНИЛИЩЕ (/SRV)</span>
                <strong class="green" id="pxe-stat-disk">${stats.disk_free_gb || 0} GB свободно</strong>
            </div>
            <div class="stat">
                <span>КЛИЕНТОВ PXE</span>
                <strong class="blue" id="pxe-stat-clients">${clients.length}</strong>
            </div>
        </div>

        <!-- Microservices Controller Cards -->
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:14px;margin-bottom:24px;">
            ${renderServiceCard('dnsmasq', 'Dnsmasq', 'DHCP Proxy + TFTP', 'UDP 67, 69, 4011', services)}
            ${renderServiceCard('nginx', 'Nginx HTTP', 'Раздача ISO и ядер на гигабите', 'TCP 80', services)}
            ${renderServiceCard('smbd', 'Samba (SMB)', 'Сетевая шара дистрибутивов WinPE', 'TCP 445', services)}
        </div>

        <!-- Inner Navigation Tabs -->
        <div class="settings-tabs" style="margin-bottom:18px;">
            <button type="button" class="settings-tab-btn ${pxeCurrentTab==='clients'?'active':''}" data-pxe-tab="clients">
                <span class="tab-icon">💻</span> <span>Подключённые клиенты (${clients.length})</span>
            </button>
            <button type="button" class="settings-tab-btn ${pxeCurrentTab==='images'?'active':''}" data-pxe-tab="images">
                <span class="tab-icon">💿</span> <span>Образы ОС и автоустановка</span>
            </button>
            <button type="button" class="settings-tab-btn ${pxeCurrentTab==='editor'?'active':''}" data-pxe-tab="editor">
                <span class="tab-icon">📝</span> <span>Редактор меню загрузки</span>
            </button>
            <button type="button" class="settings-tab-btn ${pxeCurrentTab==='logs'?'active':''}" data-pxe-tab="logs">
                <span class="tab-icon">📋</span> <span>Журнал (Live Logs)</span>
            </button>
        </div>

        <!-- Tab Body Container -->
        <div id="pxe-tab-body">
            <!-- Dynamic Content Injected Here -->
        </div>
    `;

    // Events
    $('#pxe-btn-refresh').addEventListener('click', async () => {
        toast('Обновление статуса PXE…');
        const st = await api('/pxe/status');
        renderPxeDashboard(settings, st);
    });

    $('#pxe-btn-settings').addEventListener('click', () => {
        renderPxeConnectionScreen(settings, { success: true });
    });

    content.querySelectorAll('[data-pxe-tab]').forEach(btn => {
        btn.addEventListener('click', () => {
            pxeCurrentTab = btn.dataset.pxeTab;
            content.querySelectorAll('[data-pxe-tab]').forEach(b => b.classList.toggle('active', b === btn));
            renderPxeInnerTab();
        });
    });

    // Service action handlers
    content.querySelectorAll('[data-service-action]').forEach(btn => {
        btn.addEventListener('click', async () => {
            const srv = btn.dataset.service;
            const act = btn.dataset.serviceAction;
            btn.disabled = true;
            toast(`Выполняется ${act} для ${srv}…`);
            try {
                await api(`/pxe/services/${srv}/${act}`, 'POST');
                toast(`Служба ${srv}: ${act} успешно выполнен.`);
                const st = await api('/pxe/status');
                renderPxeDashboard(settings, st);
            } catch (e) {
                toast(e.message, true);
                btn.disabled = false;
            }
        });
    });

    renderPxeInnerTab();
}

function renderServiceCard(name, title, desc, ports, services) {
    const s = services.find(x => x.name === name) || {};
    const isActive = s.active === true;
    return `
        <div style="background:#fff;border:1px solid var(--line);border-radius:10px;padding:16px;display:flex;flex-direction:column;justify-content:space-between;gap:12px;">
            <div>
                <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;">
                    <strong style="font-size:14px;color:#1e293b;">${escape(title)}</strong>
                    <span class="status ${isActive?'':'off'}" style="font-size:11px;">
                        ${isActive ? '● Работает' : '○ Остановлен'}
                    </span>
                </div>
                <div style="font-size:12px;color:var(--muted);">${escape(desc)}</div>
                <div style="font-size:11px;color:#64748b;margin-top:6px;">Порты: <code>${ports}</code></div>
            </div>
            <div style="display:flex;gap:6px;border-top:1px solid #f1f5f9;padding-top:10px;">
                <button class="secondary" data-service="${name}" data-service-action="restart" style="flex:1;font-size:11px;padding:5px 8px;">Перезапуск</button>
                ${isActive ? 
                    `<button class="danger" data-service="${name}" data-service-action="stop" style="flex:1;font-size:11px;padding:5px 8px;">Остановить</button>` :
                    `<button class="primary" data-service="${name}" data-service-action="start" style="flex:1;font-size:11px;padding:5px 8px;">Запустить</button>`
                }
            </div>
        </div>
    `;
}

// -----------------------------------------------------------------------------
// Screen 2 - Inner Tabs Router
// -----------------------------------------------------------------------------
async function renderPxeInnerTab() {
    const body = $('#pxe-tab-body');
    if (!body) return;

    if (pxeLogInterval) {
        clearInterval(pxeLogInterval);
        pxeLogInterval = null;
    }

    if (pxeCurrentTab === 'clients') {
        renderPxeClientsTab(body);
    } else if (pxeCurrentTab === 'images') {
        await renderPxeImagesTab(body);
    } else if (pxeCurrentTab === 'editor') {
        await renderPxeEditorTab(body);
    } else if (pxeCurrentTab === 'logs') {
        await renderPxeLogsTab(body);
    }
}

// -----------------------------------------------------------------------------
// TAB 1: Connected Clients Table
// -----------------------------------------------------------------------------
function renderPxeClientsTab(container) {
    const clients = pxeStatusCache?.clients || [];
    container.innerHTML = `
        <div class="panel">
            <div class="panel-head">
                <h2>Журнал сетевых запросов загрузки (DHCP Option 93 & HTTP)</h2>
                <span>Обнаружено устройств: ${clients.length}</span>
            </div>
            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th>IP АДРЕС</th>
                            <th>ХОСТ / УСТРОЙСТВО</th>
                            <th>АРХИТЕКТУРА</th>
                            <th>ЗАПРОШЕННЫЙ ЗАГРУЗЧИК</th>
                            <th>ПРОТОКОЛ</th>
                            <th>ПОСЛЕДНЯЯ АКТИВНОСТЬ</th>
                            <th>СТАТУС</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${clients.length === 0 ? `
                            <tr>
                                <td colspan="7" class="empty">
                                    <strong>Нет активных запросов сетевой загрузки</strong>
                                    Включите Network Boot (PXE) в BIOS/UEFI целевого ПК.
                                </td>
                            </tr>
                        ` : clients.map(c => `
                            <tr>
                                <td><strong style="color:var(--accent);font-family:Consolas,monospace;">${escape(c.ip)}</strong></td>
                                <td>${escape(c.hostname || 'Неизвестно')}</td>
                                <td><span class="pill blue">${escape(c.arch || 'UEFI x64')}</span></td>
                                <td><code style="color:#059669;">${escape(c.file || 'shimx64.efi')}</code></td>
                                <td>${escape(c.protocol || 'TFTP / HTTP')}</td>
                                <td>${escape(c.last_seen || '-')}</td>
                                <td><span class="status">${escape(c.status || 'Загрузка')}</span></td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        </div>
    `;
}

// -----------------------------------------------------------------------------
// TAB 2: Images Management & Unattended Customizer
// -----------------------------------------------------------------------------
async function renderPxeImagesTab(container) {
    container.innerHTML = '<div class="empty">Загрузка списка образов…</div>';

    try {
        const data = await api('/pxe/images');
        const images = data.images || [];

        container.innerHTML = `
            <!-- Action Toolbar -->
            <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:18px;flex-wrap:wrap;">
                <div style="display:flex;gap:10px;">
                    <button class="primary" id="pxe-btn-open-customizer" style="display:flex;align-items:center;gap:8px;">
                        <span>✨</span> <strong>Редактор образа (Автоустановка)</strong>
                    </button>
                    <button class="secondary" id="pxe-btn-upload-modal">
                        ＋ Загрузить ISO / образ в PXE
                    </button>
                </div>
                <div style="font-size:12px;color:var(--muted);">
                    Хранилище: <code>/srv/www/pxe/os/</code>
                </div>
            </div>

            <!-- Images Table -->
            <div class="panel">
                <div class="panel-head">
                    <h2>Установленные файлы дистрибутивов и ISO</h2>
                    <span>Всего файлов: ${images.length}</span>
                </div>
                <div class="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>ИМЯ ФАЙЛА</th>
                                <th>ПУТЬ В ХРАНИЛИЩЕ</th>
                                <th>РАЗМЕР</th>
                                <th>ДАТА ИЗМЕНЕНИЯ</th>
                                <th>ДЕЙСТВИЯ</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${images.length === 0 ? `
                                <tr>
                                    <td colspan="5" class="empty">
                                        <strong>В хранилище нет загруженных файлов</strong>
                                        Загрузите официальный образ Windows 10/11 или дистрибутив Linux.
                                    </td>
                                </tr>
                            ` : images.map(img => `
                                <tr>
                                    <td><strong>${escape(img.name)}</strong></td>
                                    <td><code style="font-size:11px;">${escape(img.path)}</code></td>
                                    <td><strong style="color:var(--accent);">${img.size_mb} MB</strong></td>
                                    <td>${escape(img.modified)}</td>
                                    <td>
                                        <button class="danger" data-delete-image="${escape(img.path)}" style="font-size:11px;padding:4px 8px;">
                                            Удалить
                                        </button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            </div>
        `;

        // Delete Image handler
        container.querySelectorAll('[data-delete-image]').forEach(btn => {
            btn.addEventListener('click', async () => {
                const path = btn.dataset.deleteImage;
                if (!confirm(`Удалить файл "${path}" с PXE-сервера?`)) return;
                try {
                    await api(`/pxe/images?path=${encodeURIComponent(path)}`, 'DELETE');
                    toast('Файл успешно удален');
                    await renderPxeImagesTab(container);
                } catch (e) {
                    toast(e.message, true);
                }
            });
        });

        // Open Upload Modal
        $('#pxe-btn-upload-modal').addEventListener('click', () => {
            showPxeUploadModal(container);
        });

        // Open Unattended Customizer Modal
        $('#pxe-btn-open-customizer').addEventListener('click', () => {
            showUnattendedCustomizerModal(images);
        });

    } catch (e) {
        container.innerHTML = `<div class="error" style="padding:20px;">Ошибка загрузки образов: ${escape(e.message)}</div>`;
    }
}

// Upload Modal
function showPxeUploadModal(parentContainer) {
    modal('Загрузка образа ОС на PXE-сервер', `
        <form id="pxe-upload-form" style="gap:14px;">
            <p style="font-size:12px;color:var(--muted);margin:0;">
                Файл загружается напрямую в хранилище PXE-сервера (без промежуточной записи на диск помощника).
            </p>
            <label>
                <span>Целевая папка</span>
                <select name="target_dir" style="width:100%;">
                    <option value="isos">os/isos/ (ISO-образы)</option>
                    <option value="windows">os/windows/ (Windows WinPE)</option>
                    <option value="debian">os/debian/ (Debian Netboot)</option>
                    <option value="clonezilla">os/clonezilla/ (Clonezilla Live)</option>
                    <option value="sysrescue">os/sysrescue/ (SystemRescue)</option>
                </select>
            </label>
            <label>
                <span>Выберите файл (.iso, .wim, .squashfs)</span>
                <input type="file" id="pxe-upload-file" required>
            </label>
            <div id="pxe-upload-progress" style="display:none;margin-top:10px;">
                <div style="display:flex;justify-content:space-between;font-size:11px;color:var(--muted);margin-bottom:4px;">
                    <span>Передача файла…</span>
                    <span id="pxe-upload-percent">0%</span>
                </div>
                <div style="background:#e2e8f0;height:6px;border-radius:3px;overflow:hidden;">
                    <div id="pxe-upload-bar" style="background:#246bfd;width:0%;height:100%;transition:width .15s;"></div>
                </div>
            </div>
            <div class="form-actions">
                <button type="submit" class="primary" id="btn-start-upload">Начать загрузку</button>
            </div>
        </form>
    `);

    $('#pxe-upload-form').addEventListener('submit', (e) => {
        e.preventDefault();
        const fileInput = $('#pxe-upload-file');
        if (!fileInput.files || !fileInput.files[0]) return;

        const file = fileInput.files[0];
        const targetDir = e.target.target_dir.value;

        const pBox = $('#pxe-upload-progress');
        const pBar = $('#pxe-upload-bar');
        const pPercent = $('#pxe-upload-percent');
        const btn = $('#btn-start-upload');

        btn.disabled = true;
        pBox.style.display = 'block';

        const formData = new FormData();
        formData.append('file', file);
        formData.append('target_dir', targetDir);

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/pxe/upload', true); // or direct PXE upload

        xhr.upload.onprogress = (evt) => {
            if (evt.lengthComputable) {
                const percent = Math.round((evt.loaded / evt.total) * 100);
                pBar.style.width = percent + '%';
                pPercent.textContent = percent + '%';
            }
        };

        xhr.onload = () => {
            if (xhr.status === 200) {
                toast(`Файл ${file.name} успешно загружен на PXE!`);
                $('#modal').close();
                renderPxeImagesTab(parentContainer);
            } else {
                toast('Ошибка при загрузке файла', true);
                btn.disabled = false;
            }
        };

        xhr.onerror = () => {
            toast('Сетевая ошибка при передаче файла', true);
            btn.disabled = false;
        };

        xhr.send(formData);
    });
}

// -----------------------------------------------------------------------------
// Unattended Image Customizer (The Core Feature)
// -----------------------------------------------------------------------------
function showUnattendedCustomizerModal(images) {
    const isoOptions = images
        .filter(img => img.name.toLowerCase().endsWith('.iso'))
        .map(img => `<option value="${escape(img.path)}">${escape(img.name)} (${img.size_mb} MB)</option>`)
        .join('');

    modal('✨ Редактор автоматической установки (Unattended Windows)', `
        <form id="customizer-form" style="gap:16px;max-height:75vh;overflow-y:auto;padding-right:4px;">
            <p style="font-size:12px;color:var(--muted);margin:0;">
                Настройка полной автоматизации Windows: от администратора потребуется только включить ПК, а по завершении компьютер будет в домене, с программами и установленным помощником.
            </p>

            <!-- 1. Source Image -->
            <label>
                <strong>1. Исходный образ Windows ISO</strong>
                <select name="isoPath" style="width:100%;">
                    ${isoOptions || '<option value="">Нет загруженных ISO (загрузите образ сначала)</option>'}
                </select>
            </label>

            <!-- 2. Computer Name -->
            <div style="background:#f8fafc;border:1px solid var(--line);border-radius:9px;padding:14px;">
                <strong style="font-size:13px;display:block;margin-bottom:8px;">2. Имя компьютера</strong>
                <label style="flex-direction:row;align-items:center;gap:8px;margin-bottom:6px;">
                    <input type="radio" name="nameMode" value="prompt" checked>
                    <span>Запрашивать у техника в окне перед стартом WinPE</span>
                </label>
                <label style="flex-direction:row;align-items:center;gap:8px;margin-bottom:6px;">
                    <input type="radio" name="nameMode" value="pattern">
                    <span>Автоматический шаблон:</span>
                    <input name="namePattern" value="CLINIC-PC-%SERIAL%" style="padding:4px 8px;font-size:12px;width:180px;">
                </label>
            </div>

            <!-- 3. Active Directory Domain Join -->
            <div style="background:#f8fafc;border:1px solid var(--line);border-radius:9px;padding:14px;">
                <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;">
                    <strong style="font-size:13px;">3. Ввод в Active Directory</strong>
                    <label style="flex-direction:row;align-items:center;gap:6px;font-size:11px;">
                        <input type="checkbox" name="adJoin" checked>
                        <span>Ввести в домен автоматически</span>
                    </label>
                </div>
                <div class="form-row">
                    <label>
                        <span>Имя домена</span>
                        <input name="adDomain" value="gp1.loc">
                    </label>
                    <label>
                        <span>Целевой OU (опционально)</span>
                        <input name="adOu" placeholder="OU=Workstations,DC=gp1,DC=loc">
                    </label>
                </div>
                <div class="form-row" style="margin-top:8px;">
                    <label>
                        <span>Учётная запись для ввода (Domain\\User)</span>
                        <input name="adUser" placeholder="GP1\\join_admin">
                    </label>
                    <label>
                        <span>Пароль</span>
                        <input type="password" name="adPassword" placeholder="••••••••">
                    </label>
                </div>
            </div>

            <!-- 4. Silent Software Installation (.MSI and .EXE) -->
            <div style="background:#f8fafc;border:1px solid var(--line);border-radius:9px;padding:14px;">
                <strong style="font-size:13px;display:block;margin-bottom:8px;">4. Тихая установка программ (.MSI и .EXE)</strong>
                
                <div style="background:#edf8f3;border:1px solid #bbf7d0;border-radius:7px;padding:8px 12px;font-size:12px;color:#166534;margin-bottom:10px;display:flex;align-items:center;gap:8px;">
                    <input type="checkbox" name="installPixelHelper" checked disabled>
                    <span><strong>PixelHelper.msi</strong> — автоматическая установка агента и постановка на учёт (обязательно)</span>
                </div>

                <div id="custom-apps-list" style="display:grid;gap:8px;margin-bottom:10px;">
                    <!-- Rows added dynamically -->
                </div>

                <button type="button" class="secondary" id="btn-add-app-row" style="font-size:11px;padding:5px 10px;">
                    ＋ Добавить программу (.exe / .msi)
                </button>
            </div>

            <div id="customizer-status" style="display:none;background:#0f172a;color:#38bdf8;padding:12px;border-radius:8px;font-family:Consolas,monospace;font-size:11px;max-height:140px;overflow-y:auto;white-space:pre-wrap;"></div>

            <div class="form-actions">
                <button type="submit" class="primary" id="btn-run-customizer">
                    🚀 Сгенерировать и внедрить в WinPE
                </button>
            </div>
        </form>
    `);

    // Add app row helper
    const appsList = $('#custom-apps-list');
    const addAppRow = (name = '', path = '', args = '') => {
        const row = document.createElement('div');
        row.style.cssText = 'display:grid;grid-template-columns:1fr 2fr 1.5fr auto;gap:6px;align-items:center;';
        row.innerHTML = `
            <input placeholder="Название (Chrome)" value="${escape(name)}" class="app-name" style="font-size:11px;padding:6px;">
            <input placeholder="Путь: \\\\server\\install\\soft\\app.exe" value="${escape(path)}" class="app-path" style="font-size:11px;padding:6px;">
            <input placeholder="Ключи тихой: /S, /quiet, /qn" value="${escape(args)}" class="app-args" style="font-size:11px;padding:6px;">
            <button type="button" class="danger" style="padding:4px 8px;font-size:11px;">×</button>
        `;
        row.querySelector('button').addEventListener('click', () => row.remove());
        appsList.appendChild(row);
    };

    // Prepopulate standard clinic apps
    addAppRow('Google Chrome', '\\\\172.16.16.6\\install\\soft\\chrome.exe', '/silent /install');
    addAppRow('7-Zip', '\\\\172.16.16.6\\install\\soft\\7z.msi', '/qn /norestart');

    $('#btn-add-app-row').addEventListener('click', () => addAppRow());

    // Submit handler
    $('#customizer-form').addEventListener('submit', async (e) => {
        e.preventDefault();
        const f = e.target;
        const statusBox = $('#customizer-status');
        const runBtn = $('#btn-run-customizer');

        if (!f.isoPath.value) {
            toast('Выберите файл ISO для обработки', true);
            return;
        }

        runBtn.disabled = true;
        statusBox.style.display = 'block';
        statusBox.textContent = `[INFO] Старт генерации WinPE для ${f.isoPath.value}…\nИзвлечение файлов, внедрение autounattend.xml и SetupComplete.cmd…\nПожалуйста, подождите (2-4 минуты)…`;

        // Collect custom apps
        const apps = [];
        appsList.querySelectorAll('div').forEach(row => {
            const n = row.querySelector('.app-name').value.trim();
            const p = row.querySelector('.app-path').value.trim();
            const a = row.querySelector('.app-args').value.trim();
            if (p) apps.push({ name: n || 'App', path: p, args: a });
        });

        const payload = {
            iso_path: f.isoPath.value,
            name_mode: f.nameMode.value,
            name_pattern: f.namePattern?.value || 'PC-%RANDOM%',
            ad_join: f.adJoin.checked,
            ad_domain: f.adDomain.value,
            ad_ou: f.adOu.value,
            ad_user: f.adUser.value,
            ad_password: f.adPassword.value,
            custom_apps: apps
        };

        try {
            const res = await api('/pxe/process-windows', 'POST', payload);

            if (res.status === 'ok') {
                statusBox.textContent = `[УСПЕХ] WinPE успешно сформирован и настроен на автоустановку!\n\n${res.stdout || ''}`;
                toast('Образ Windows успешно подготовлен к сетевой установке!');
            } else {
                statusBox.textContent = `[ОШИБКА] Обработка не удалась:\n${res.stderr || res.stdout || ''}`;
                toast('Ошибка при сборке образа', true);
            }
        } catch (err) {
            statusBox.textContent = `[ОШИБКА] Сбой соединения: ${err.message}`;
            toast(err.message, true);
        } finally {
            runBtn.disabled = false;
        }
    });
}

// -----------------------------------------------------------------------------
// TAB 3: Boot Menu Editor
// -----------------------------------------------------------------------------
async function renderPxeEditorTab(container) {
    container.innerHTML = '<div class="empty">Загрузка конфигурации…</div>';

    try {
        const configData = await api(`/pxe/configs/${pxeCurrentConfig}`);
        
        container.innerHTML = `
            <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:14px;flex-wrap:wrap;">
                <div style="display:flex;gap:6px;">
                    <button class="pxe-cfg-btn ${pxeCurrentConfig==='grub'?'primary':'secondary'}" data-cfg="grub">grub.cfg (UEFI)</button>
                    <button class="pxe-cfg-btn ${pxeCurrentConfig==='syslinux'?'primary':'secondary'}" data-cfg="syslinux">pxelinux.cfg (BIOS)</button>
                    <button class="pxe-cfg-btn ${pxeCurrentConfig==='dnsmasq'?'primary':'secondary'}" data-cfg="dnsmasq">dnsmasq-pxe.conf</button>
                    <button class="pxe-cfg-btn ${pxeCurrentConfig==='preseed'?'primary':'secondary'}" data-cfg="preseed">preseed.cfg (Debian)</button>
                </div>
                <button class="primary" id="pxe-btn-save-cfg">
                    💾 Сохранить и применить
                </button>
            </div>

            <div class="panel" style="padding:0;overflow:hidden;">
                <textarea id="pxe-cfg-textarea" style="width:100%;height:500px;font-family:Consolas,monospace;font-size:12px;background:#0f172a;color:#7dd3fc;border:none;padding:16px;box-sizing:border-box;resize:vertical;"></textarea>
            </div>
        `;

        $('#pxe-cfg-textarea').value = configData.content || '';

        // Switch config tab
        container.querySelectorAll('[data-cfg]').forEach(btn => {
            btn.addEventListener('click', async () => {
                pxeCurrentConfig = btn.dataset.cfg;
                await renderPxeEditorTab(container);
            });
        });

        // Save config
        $('#pxe-btn-save-cfg').addEventListener('click', async () => {
            const content = $('#pxe-cfg-textarea').value;
            toast('Сохранение конфигурации…');
            try {
                await api(`/pxe/configs/${pxeCurrentConfig}`, 'POST', { content });
                toast(`Конфигурация ${pxeCurrentConfig} успешно сохранена и синхронизирована!`);
            } catch (e) {
                toast(e.message, true);
            }
        });

    } catch (e) {
        container.innerHTML = `<div class="error" style="padding:20px;">Ошибка загрузки конфигурации: ${escape(e.message)}</div>`;
    }
}

// -----------------------------------------------------------------------------
// TAB 4: Live Server Logs
// -----------------------------------------------------------------------------
async function renderPxeLogsTab(container) {
    container.innerHTML = `
        <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:14px;flex-wrap:wrap;">
            <div style="display:flex;gap:6px;">
                <button class="pxe-log-btn ${pxeCurrentLog==='dnsmasq'?'primary':'secondary'}" data-log="dnsmasq">Dnsmasq (DHCP/TFTP)</button>
                <button class="pxe-log-btn ${pxeCurrentLog==='nginx-access'?'primary':'secondary'}" data-log="nginx-access">Nginx Access Log</button>
                <button class="pxe-log-btn ${pxeCurrentLog==='smbd'?'primary':'secondary'}" data-log="smbd">Samba (SMB)</button>
            </div>
            <div style="display:flex;align-items:center;gap:10px;">
                <label style="flex-direction:row;align-items:center;gap:6px;font-size:12px;color:var(--muted);">
                    <input type="checkbox" id="pxe-log-autoscroll" checked>
                    <span>Автопрокрутка</span>
                </label>
                <button class="secondary" id="pxe-btn-refresh-log" style="font-size:11px;padding:6px 10px;">↻ Обновить</button>
            </div>
        </div>

        <div class="terminal" id="pxe-terminal" style="height:520px;background:#050c14;color:#a5f3fc;">Загрузка логов…</div>
    `;

    const term = $('#pxe-terminal');

    const fetchLogs = async () => {
        try {
            const res = await api(`/pxe/logs/${pxeCurrentLog}`);
            term.textContent = res.logs || 'Нет свежих записей в логах.';
            if ($('#pxe-log-autoscroll')?.checked) {
                term.scrollTop = term.scrollHeight;
            }
        } catch (e) {
            term.textContent = `[ОШИБКА ПОЛУЧЕНИЯ ЛОГОВ]: ${e.message}`;
        }
    };

    container.querySelectorAll('[data-log]').forEach(btn => {
        btn.addEventListener('click', async () => {
            pxeCurrentLog = btn.dataset.log;
            container.querySelectorAll('[data-log]').forEach(b => b.className = b.dataset.log === pxeCurrentLog ? 'primary' : 'secondary');
            await fetchLogs();
        });
    });

    $('#pxe-btn-refresh-log').addEventListener('click', fetchLogs);

    await fetchLogs();
    pxeLogInterval = setInterval(fetchLogs, 3500);
}

// Global window registration
window.renderPxePage = renderPxePage;


