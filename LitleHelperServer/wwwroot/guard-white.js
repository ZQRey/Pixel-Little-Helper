let eventSource = null;
let isAlertActive = false;
let remainingSeconds = 600;

function updateTimerDisplay() {
    const m = Math.floor(Math.max(0, remainingSeconds) / 60);
    const s = Math.max(0, remainingSeconds) % 60;
    const timerEl = document.getElementById('alertTimer');
    if (timerEl) {
        timerEl.innerText = 'Автосброс тревоги через: ' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
    }
}

function normalizeState(data) {
    if (!data) return null;
    const active = Boolean(data.active ?? data.Active);
    const cabinet = data.cabinet ?? data.Cabinet ?? '';
    const branch = data.branchName ?? data.BranchName ?? data.branch ?? data.Branch ?? '';
    const time = data.time ?? data.Time ?? '';
    const accepted = Boolean(data.accepted ?? data.Accepted);
    const acceptedBy = data.acceptedBy ?? data.AcceptedBy ?? '';
    const client = data.client ?? data.Client ?? data.username ?? data.Username ?? '';
    const department = data.department ?? data.Department ?? '';
    const rem = data.remainingSeconds ?? data.RemainingSeconds ?? 600;
    return { active, cabinet, branch, time, accepted, acceptedBy, client, department, remainingSeconds: rem };
}

function setAlert(data) {
    const connEl = document.getElementById('connection');
    const alertEl = document.getElementById('alert');
    const cabinetEl = document.getElementById('cabinet');
    const detailsEl = document.getElementById('details');
    const statusEl = document.getElementById('status');

    if (data && data.active) {
        isAlertActive = true;
        remainingSeconds = (data.remainingSeconds && data.remainingSeconds > 0) ? data.remainingSeconds : 600;
        updateTimerDisplay();

        if (alertEl) alertEl.hidden = false;
        if (cabinetEl) cabinetEl.textContent = 'КАБИНЕТ № ' + (data.cabinet || 'Не указан');

        const details = [data.branch, data.department, data.client, data.time].filter(Boolean).join(' · ');
        if (detailsEl) detailsEl.textContent = details;

        if (statusEl) {
            statusEl.textContent = data.accepted ? ('✓ Вызов принят' + (data.acceptedBy ? ': ' + data.acceptedBy : ' охраной')) : '';
        }
    } else {
        isAlertActive = false;
        if (alertEl) alertEl.hidden = true;
        if (cabinetEl) cabinetEl.textContent = '';
        if (detailsEl) detailsEl.textContent = '';
        if (statusEl) statusEl.textContent = '';
    }
}

function connectSSE() {
    if (eventSource) {
        try { eventSource.close(); } catch (e) {}
    }
    const connEl = document.getElementById('connection');
    eventSource = new EventSource('/api/guard/white-events');

    eventSource.addEventListener('state', (e) => {
        if (connEl) connEl.style.background = '#062c0a';
        try {
            const raw = JSON.parse(e.data);
            const st = normalizeState(raw);
            setAlert(st);
        } catch (err) { console.error(err); }
    });

    eventSource.addEventListener('alert', (e) => {
        if (connEl) connEl.style.background = '#062c0a';
        try {
            const raw = JSON.parse(e.data);
            const st = normalizeState(raw);
            setAlert(st);
        } catch (err) { console.error(err); }
    });

    eventSource.addEventListener('reset', () => {
        setAlert(null);
    });

    eventSource.onopen = () => {
        if (connEl) connEl.style.background = '#062c0a';
    };

    eventSource.onerror = () => {
        if (connEl) connEl.style.background = '#340404';
        try { eventSource.close(); } catch (e) {}
        setTimeout(connectSSE, 3000);
    };
}

// Initial SSE connection
connectSSE();

// Fallback polling every 5 seconds with normalization
setInterval(async () => {
    try {
        const res = await fetch('/api/guard/white-state');
        if (res.ok) {
            const raw = await res.json();
            const st = normalizeState(raw);
            if (st && st.active && !isAlertActive) {
                setAlert(st);
            } else if ((!st || !st.active) && isAlertActive) {
                setAlert(null);
            } else if (st && st.active && isAlertActive) {
                const statusEl = document.getElementById('status');
                if (statusEl) {
                    statusEl.textContent = st.accepted ? ('✓ Вызов принят' + (st.acceptedBy ? ': ' + st.acceptedBy : ' охраной')) : '';
                }
            }
        }
    } catch (e) {}
}, 5000);
