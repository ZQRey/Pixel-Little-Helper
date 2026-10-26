// PixelHelper Standalone Video/Audio Call Interface
const $ = id => document.getElementById(id);
const params = new URLSearchParams(window.location.search);

const callState = {
  peerId: parseInt(params.get('peer') || '0', 10),
  isVideo: params.get('video') === '1',
  isCaller: params.get('caller') === '1',
  currentUserId: parseInt(params.get('user') || '0', 10),
  targetName: params.get('name') || 'Собеседник',
  localPort: parseInt(params.get('port') || '49215', 10),
  callId: params.get('callId') || (params.get('caller') === '1' ? 'call_' + Date.now() + '_' + Math.random().toString(36).slice(2, 8) : null),
  token: params.get('token') || '',
  isGroup: parseInt(params.get('peer') || '0', 10) < 0 || params.get('adHoc') === '1',
  groupId: parseInt(params.get('peer') || '0', 10) < 0 ? -parseInt(params.get('peer') || '0', 10) : 0,
  peers: new Map(), // userId -> { userId, fullName, pc, stream, queuedCandidates: [], audioDetector: null }
  roster: [],
  myHandRaised: false,
  pc: null,
  ws: null,
  localStream: null,
  remoteStream: null,
  startTime: null,
  timerInterval: null,
  ringtone: null,
  recorder: null,
  recordChunks: [],
  recordStartTime: null,
  recordInterval: null,
  isRecording: false,
  queuedCandidates: [],
  callTimeout: null,
  activeMicDeviceId: null,
  mode: 'mesh',
  livekitRoom: null,
  isScreenSharing: false,
  remoteScreenSharing: false,
  screenStream: null
};

// UI Elements
const targetNameEl = $('call-target-name');
const statusEl = $('call-status');
const timerEl = $('call-timer');
const recordIndicator = $('record-indicator');
const recordTimerEl = $('record-timer');
const remoteVideo = $('remote-video');
const localVideo = $('local-video');
const remoteAvatarFallback = $('remote-avatar-fallback');
const remoteInitials = $('remote-initials');
const remoteFallbackName = $('remote-fallback-name');
const videoContainer = $('video-container');
const groupGrid = $('group-grid');
const toastEl = $('toast');

const btnMic = $('btn-toggle-mic');
const btnCam = $('btn-toggle-cam');
const btnScreen = $('btn-toggle-screen');
const btnAddParticipant = $('btn-add-participant');
const btnRaiseHand = $('btn-raise-hand');
const btnParticipants = $('btn-participants');
const btnParticipantsLabel = $('btn-participants-label');
const btnRecord = $('btn-record');
const btnDeviceSettings = $('btn-device-settings');
const btnHangup = $('btn-hangup');

const rosterModal = $('roster-modal');
const btnCloseRosterModal = $('btn-close-roster-modal');
const rosterList = $('roster-list');
const hostActions = $('host-actions');
const btnEndForAll = $('btn-end-for-all');
const btnInviteFromRoster = $('btn-invite-from-roster');

const inviteModal = $('invite-modal');
const btnCloseInviteModal = $('btn-close-invite-modal');
const inviteSearchInput = $('invite-search-input');
const inviteCandidatesList = $('invite-candidates-list');

const deviceModal = $('device-modal');
const btnCloseDeviceModal = $('btn-close-device-modal');
const callMicSelect = $('call-mic-select');
const callMicMeterFill = $('call-mic-meter-fill');

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function showToast(msg, duration = 4000) {
  toastEl.textContent = msg;
  toastEl.hidden = false;
  setTimeout(() => { toastEl.hidden = true; }, duration);
}

function setRemoteAvatarFallbackVisible(visible) {
  if (!remoteAvatarFallback) return;
  remoteAvatarFallback.hidden = !visible;
  remoteAvatarFallback.style.display = visible ? 'flex' : 'none';
}

function getInitials(name) {
  if (!name) return 'PH';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase();
}

// 1. Инициализация интерфейса
targetNameEl.textContent = callState.targetName;
remoteFallbackName.textContent = callState.targetName;
remoteInitials.textContent = getInitials(callState.targetName);

// Настройка кнопок в зависимости от типа звонка (1-на-1 или группа)
if (callState.isGroup) {
  btnRaiseHand.hidden = false;
  btnParticipants.hidden = false;
  btnRecord.hidden = !callState.isCaller;
  videoContainer.hidden = true;
  groupGrid.hidden = false;
} else {
  btnRaiseHand.hidden = true;
  btnParticipants.hidden = false;
  btnRecord.hidden = !callState.isCaller;
  videoContainer.hidden = false;
  groupGrid.hidden = true;
  setRemoteAvatarFallbackVisible(!callState.isVideo);
}

if (btnAddParticipant) {
  btnAddParticipant.hidden = false;
}

if (!callState.isVideo) {
  btnCam.classList.add('muted');
  btnCam.querySelector('.icon').textContent = '🚫';
  $('local-pip-container').style.display = 'none';
  setRemoteAvatarFallbackVisible(true);
}

// 2. Звук вызова (рингтон)
function playRingtone() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    let count = 0;
    const ringInterval = setInterval(() => {
      if (ctx.state === 'suspended') ctx.resume();
      const osc1 = ctx.createOscillator();
      const osc2 = ctx.createOscillator();
      const gain = ctx.createGain();
      osc1.frequency.setValueAtTime(425, ctx.currentTime);
      osc2.frequency.setValueAtTime(450, ctx.currentTime);
      const now = ctx.currentTime;
      gain.gain.setValueAtTime(0.12, now);
      gain.gain.setValueAtTime(0, now + 1.2);
      osc1.connect(gain); osc2.connect(gain); gain.connect(ctx.destination);
      osc1.start(now); osc1.stop(now + 1.2); osc2.start(now); osc2.stop(now + 1.2);
      count++;
      if (count > 30) stopRingtone();
    }, 3000);
    callState.ringtone = { ctx, ringInterval };
  } catch {}
}

function stopRingtone() {
  if (callState.ringtone) {
    clearInterval(callState.ringtone.ringInterval);
    if (callState.ringtone.ctx) callState.ringtone.ctx.close().catch(() => {});
    callState.ringtone = null;
  }
}

// 3. Таймер звонка
function startCallTimer() {
  callState.startTime = Date.now();
  statusEl.textContent = 'Идёт разговор';
  clearInterval(callState.timerInterval);
  callState.timerInterval = setInterval(() => {
    const elapsed = Math.floor((Date.now() - callState.startTime) / 1000);
    const m = String(Math.floor(elapsed / 60)).padStart(2, '0');
    const s = String(elapsed % 60).padStart(2, '0');
    timerEl.textContent = `${m}:${s}`;
  }, 1000);
}

// 4. Захват медиа
async function acquireLocalMedia() {
  HelperMedia.requireDevices();
  let audioError = null;
  let videoError = null;
  let audioTrack = null;
  let videoTrack = null;

  // 1. Попытка захватить микрофон с учетом переданного параметра или сохраненного устройства
  try {
    if (navigator.mediaDevices?.getUserMedia) {
      const micParam = params.get('mic');
      let targetDeviceId = localStorage.getItem('helper.chat.mic_id') || undefined;

      if (micParam && navigator.mediaDevices.enumerateDevices) {
        try {
          const devs = await navigator.mediaDevices.enumerateDevices();
          const match = devs.find(d => d.kind === 'audioinput' && d.label && d.label.toLowerCase().includes(micParam.toLowerCase()));
          if (match) {
            targetDeviceId = match.deviceId;
          }
        } catch {}
      }

      const audioConstraints = {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true
      };
      if (targetDeviceId) {
        audioConstraints.deviceId = { ideal: targetDeviceId };
      }

      const aStream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraints });
      audioTrack = aStream.getAudioTracks()[0] || null;
      if (audioTrack) {
        const s = audioTrack.getSettings ? audioTrack.getSettings() : {};
        callState.activeMicDeviceId = s.deviceId || targetDeviceId || null;
      }
    }
  } catch (e) {
    console.warn('Микрофон недоступен с предпочтительными настройками, fallback:', e);
    try {
      const aStream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioTrack = aStream.getAudioTracks()[0] || null;
    } catch (e2) {
      audioError = e2;
      console.warn('Микрофон полностью недоступен:', e2);
    }
  }

  // 2. Попытка захватить камеру если видеозвонок
  if (callState.isVideo) {
    try {
      if (navigator.mediaDevices?.getUserMedia) {
        const vStream = await navigator.mediaDevices.getUserMedia({
          video: { width: { ideal: 1280 }, height: { ideal: 720 } }
        });
        videoTrack = vStream.getVideoTracks()[0] || null;
      }
    } catch (err) {
      console.warn('Камера с идеальным разрешением не найдена:', err);
      try {
        const vStream2 = await navigator.mediaDevices.getUserMedia({ video: true });
        videoTrack = vStream2.getVideoTracks()[0] || null;
      } catch (err2) {
        videoError = err2;
        console.warn('Камера не обнаружена:', err2);
      }
    }
  }

  // 3. Если микрофон отсутствует, создаём резервную тишину для работы WebRTC
  const hasMicrophone = !!audioTrack;
  if (!audioTrack) {
    try {
      const ctx = new (window.AudioContext || window.webkitAudioContext)();
      const osc = ctx.createOscillator();
      const dst = ctx.createMediaStreamDestination();
      const gain = ctx.createGain();
      gain.gain.value = 0;
      osc.connect(gain);
      gain.connect(dst);
      osc.start();
      audioTrack = dst.stream.getAudioTracks()[0] || null;
    } catch {}
  }

  const tracks = [];
  if (audioTrack) tracks.push(audioTrack);
  if (videoTrack) tracks.push(videoTrack);

  const stream = new MediaStream(tracks);
  callState.localStream = stream;

  if (videoTrack) {
    localVideo.srcObject = stream;
  } else {
    const wasVideo = callState.isVideo;
    callState.isVideo = false;
    const pip = $('local-pip-container');
    if (pip) pip.style.display = 'none';
    btnCam.classList.add('muted');
    btnCam.querySelector('.icon').textContent = '🚫';
    if (wasVideo) showToast(HelperMedia.explain(videoError, 'Камера'));
  }

  if (!hasMicrophone) {
    showToast(HelperMedia.explain(audioError, 'Микрофон'));
  }

  return stream;
}

// 5. STUN/TURN конфигурация
async function getRtcConfig() {
  try {
    const headers = {};
    if (callState.token) {
      headers['Authorization'] = 'Bearer ' + callState.token;
    }
    const res = await fetch('/api/messenger/webrtc/config', { headers });
    if (res.ok) {
      const data = await res.json();
      return { iceServers: data.iceServers || [{ urls: 'stun:stun.l.google.com:19302' }] };
    }
  } catch {}
  return { iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] };
}

async function flushQueuedCandidates() {
  if (!callState.pc || !callState.pc.remoteDescription) return;
  const queued = [...callState.queuedCandidates];
  callState.queuedCandidates = [];
  for (const cand of queued) {
    try {
      await callState.pc.addIceCandidate(new RTCIceCandidate(cand));
    } catch (err) {
      console.warn('Ошибка добавления очереди ICE-кандидата:', err);
    }
  }
}

function findVideoSender(pc) {
  if (!pc) return null;
  if (typeof pc.getTransceivers === 'function') {
    const tr = pc.getTransceivers().find(t =>
      (t.sender && ((t.sender.track && t.sender.track.kind === 'video') || t.sender.kind === 'video')) ||
      (t.receiver && t.receiver.track && t.receiver.track.kind === 'video') ||
      (t.mid !== null && t.receiver && t.receiver.track && t.receiver.track.kind === 'video')
    );
    if (tr && tr.sender) return tr.sender;
  }
  const senders = typeof pc.getSenders === 'function' ? pc.getSenders() : [];
  return senders.find(s => (s.track && s.track.kind === 'video') || s.kind === 'video') || null;
}

async function renegotiateCall(pc, targetUserId) {
  if (!pc || !callState.ws || callState.ws.readyState !== WebSocket.OPEN) return;
  if (pc.signalingState !== 'stable') {
    console.warn('renegotiateCall: signalingState is', pc.signalingState);
    return;
  }
  try {
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    callState.ws.send(JSON.stringify({
      type: 1,
      target: 'SendSdpOffer',
      arguments: [callState.callId, targetUserId, offer.sdp]
    }) + '\u001e');
  } catch (e) {
    console.warn('renegotiateCall error:', e);
  }
}

function ensureRemoteVideoAttached() {
  if (!callState.pc) return false;
  if (!callState.remoteStream) {
    callState.remoteStream = new MediaStream();
  }

  let hasVideo = false;
  if (typeof callState.pc.getReceivers === 'function') {
    callState.pc.getReceivers().forEach(r => {
      if (r.track) {
        if (!callState.remoteStream.getTracks().some(t => t.id === r.track.id)) {
          callState.remoteStream.addTrack(r.track);
        }
        if (r.track.kind === 'video') {
          hasVideo = true;
          r.track.enabled = true;
          r.track.onunmute = () => {
            if (callState.remoteScreenSharing) {
              remoteVideo.style.objectFit = 'contain';
              setRemoteAvatarFallbackVisible(false);
            }
            remoteVideo.play().catch(() => {});
          };
        }
      }
    });
  }

  if (remoteVideo.srcObject !== callState.remoteStream) {
    remoteVideo.srcObject = callState.remoteStream;
  }

  remoteVideo.play().catch(e => {
    console.warn('remoteVideo.play error:', e);
  });

  return hasVideo;
}

// 6. Подключение WebRTC PeerConnection
async function setupPeerConnection(callId, targetUserId) {
  const cfg = await getRtcConfig();
  const pc = new RTCPeerConnection(cfg);
  callState.pc = pc;
  callState.callId = callId;

  if (callState.localStream) {
    callState.localStream.getTracks().forEach(track => pc.addTrack(track, callState.localStream));
  }

  // Всегда резервируем видеоканал (m=video sendrecv) даже при отсутствии камеры для мгновенного шаринга экрана
  const hasVideoTrack = callState.localStream && callState.localStream.getVideoTracks().length > 0;
  if (!hasVideoTrack) {
    try {
      pc.addTransceiver('video', { direction: 'sendrecv' });
    } catch (e) {
      console.warn('addTransceiver error:', e);
    }
  }

  pc.ontrack = e => {
    if (!callState.remoteStream) {
      callState.remoteStream = (e.streams && e.streams[0]) ? e.streams[0] : new MediaStream();
    }
    if (e.track && !callState.remoteStream.getTracks().some(t => t.id === e.track.id)) {
      callState.remoteStream.addTrack(e.track);
    }
    if (remoteVideo.srcObject !== callState.remoteStream) {
      remoteVideo.srcObject = callState.remoteStream;
    }
    remoteVideo.play().catch(() => {});

    const vTracks = callState.remoteStream.getVideoTracks();
    const vTrack = vTracks[0] || (e.track?.kind === 'video' ? e.track : null);
    if (vTrack) {
      const showFallback = !callState.remoteScreenSharing && (!vTrack.enabled || vTrack.muted);
      setRemoteAvatarFallbackVisible(showFallback);
      vTrack.onmute = () => {
        if (!callState.remoteScreenSharing) setRemoteAvatarFallbackVisible(true);
      };
      vTrack.onunmute = () => {
        setRemoteAvatarFallbackVisible(false);
        remoteVideo.play().catch(() => {});
      };
    } else {
      setRemoteAvatarFallbackVisible(!callState.remoteScreenSharing);
    }
  };

  pc.onicecandidate = e => {
    if (e.candidate && callState.ws?.readyState === WebSocket.OPEN) {
      callState.ws.send(JSON.stringify({
        type: 1,
        target: 'SendIceCandidate',
        arguments: [callId, targetUserId, e.candidate.candidate, e.candidate.sdpMid || '', e.candidate.sdpMLineIndex || 0]
      }) + '\u001e');
    }
  };

  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'connected') {
      stopRingtone();
      startCallTimer();
    } else if (pc.connectionState === 'disconnected') {
      statusEl.textContent = 'Переподключение…';
    } else if (pc.connectionState === 'failed') {
      statusEl.textContent = 'Связь прервана, перезапуск…';
      try {
        if (typeof pc.restartIce === 'function') {
          pc.restartIce();
        }
      } catch {}
    }
  };

  return pc;
}

async function flushQueuedCandidatesForPeer(peer) {
  if (!peer || !peer.pc || !peer.pc.remoteDescription) return;
  const queued = [...peer.queuedCandidates];
  peer.queuedCandidates = [];
  for (const cand of queued) {
    try {
      await peer.pc.addIceCandidate(new RTCIceCandidate(cand));
    } catch (err) {
      console.warn('Ошибка добавления ICE-кандидата в mesh peer:', err);
    }
  }
}

// Активный говорящий (подсветка рамки тайла при звуке)
function attachAudioDetector(stream, tileEl) {
  const audioTracks = stream.getAudioTracks();
  if (!audioTracks.length) return null;
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const src = ctx.createMediaStreamSource(new MediaStream([audioTracks[0]]));
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 64;
    src.connect(analyser);
    const data = new Uint8Array(analyser.frequencyBinCount);
    const interval = setInterval(() => {
      analyser.getByteFrequencyData(data);
      let sum = 0;
      for (let i = 0; i < data.length; i++) sum += data[i];
      const avg = sum / data.length;
      if (avg > 15) {
        tileEl.classList.add('speaking');
      } else {
        tileEl.classList.remove('speaking');
      }
    }, 150);
    return { ctx, interval };
  } catch (e) {
    return null;
  }
}

function updateGridCount() {
  if (!callState.isGroup || !groupGrid) return;
  const count = groupGrid.querySelectorAll('.video-tile').length;
  groupGrid.dataset.count = Math.min(6, Math.max(1, count));
}

function addLocalTileToGrid() {
  if (!callState.isGroup || !groupGrid) return;
  let tile = $('tile-local');
  if (!tile) {
    tile = document.createElement('div');
    tile.id = 'tile-local';
    tile.className = 'video-tile local-tile';
    tile.innerHTML = `
      <video id="local-grid-video" autoplay playsinline muted></video>
      <div id="local-grid-fallback" class="avatar-fallback" hidden>
        <div class="avatar-circle"><span>Вы</span></div>
      </div>
      <div class="tile-bar">
        <span class="tile-name">Вы</span>
        <div class="tile-icons">
          <span id="local-tile-hand" class="tile-hand-badge" hidden>✋</span>
          <span id="local-tile-mic">🎤</span>
          <span id="local-tile-cam">📷</span>
        </div>
      </div>
    `;
    groupGrid.appendChild(tile);
  }
  const v = $('local-grid-video');
  if (v && callState.localStream) {
    v.srcObject = callState.localStream;
  }
  updateGridCount();
}

function addRemoteTileToGrid(userId, fullName) {
  if (!callState.isGroup || !groupGrid) return null;
  let tile = $(`tile-${userId}`);
  if (!tile) {
    tile = document.createElement('div');
    tile.id = `tile-${userId}`;
    tile.className = 'video-tile';
    tile.innerHTML = `
      <video id="video-${userId}" autoplay playsinline></video>
      <div id="fallback-${userId}" class="avatar-fallback">
        <div class="avatar-circle"><span>${getInitials(fullName)}</span></div>
        <span class="fallback-name">${fullName}</span>
      </div>
      <div class="tile-bar">
        <span class="tile-name">${fullName}</span>
        <div class="tile-icons">
          <span id="hand-${userId}" class="tile-hand-badge" hidden>✋</span>
          <span id="mic-${userId}">🎤</span>
          <span id="cam-${userId}">📷</span>
        </div>
      </div>
    `;
    groupGrid.appendChild(tile);
    updateGridCount();
  }
  return tile;
}

function removeRemoteTileFromGrid(userId) {
  const tile = $(`tile-${userId}`);
  if (tile) {
    tile.remove();
    updateGridCount();
  }
}

function updateRosterModal() {
  if (!rosterList) return;
  rosterList.replaceChildren();

  // Число участников на кнопке
  const totalCount = callState.roster.length;
  if (btnParticipantsLabel) {
    btnParticipantsLabel.textContent = totalCount > 0 ? `Участники (${totalCount})` : 'Участники';
  }

  callState.roster.forEach(p => {
    const item = document.createElement('div');
    item.className = 'roster-item';
    const isMe = p.userId === callState.currentUserId;
    const isHost = p.isCaller || p.userId === callState.currentUserId && callState.isCaller;

    item.innerHTML = `
      <div class="roster-user-info">
        <div class="roster-avatar">${getInitials(p.fullName)}</div>
        <div class="roster-name-wrap">
          <span class="roster-name">${p.fullName} ${isMe ? '(Вы)' : ''}</span>
          <span class="roster-role">${isHost ? '👑 Организатор' : 'Участник'}</span>
        </div>
      </div>
      <div class="roster-badges">
        <span id="roster-hand-${p.userId}" ${p.handRaised ? '' : 'hidden'}>✋</span>
      </div>
    `;
    rosterList.appendChild(item);
  });

  if (hostActions) {
    hostActions.hidden = !callState.isCaller;
  }
}

async function setupPeerConnectionForUser(userId, fullName, isOfferer) {
  let peer = callState.peers.get(userId);
  if (peer && peer.pc) return peer;

  addRemoteTileToGrid(userId, fullName);

  const cfg = await getRtcConfig();
  const pc = new RTCPeerConnection(cfg);
  peer = {
    userId,
    fullName,
    pc,
    stream: null,
    queuedCandidates: (peer && peer.queuedCandidates) ? peer.queuedCandidates : [],
    audioDetector: null
  };
  callState.peers.set(userId, peer);

  if (callState.localStream) {
    callState.localStream.getTracks().forEach(track => pc.addTrack(track, callState.localStream));
  }

  const hasVideoTrack = callState.localStream && callState.localStream.getVideoTracks().length > 0;
  if (!hasVideoTrack) {
    try {
      pc.addTransceiver('video', { direction: 'sendrecv' });
    } catch (e) {
      console.warn('addTransceiver error for mesh peer:', e);
    }
  }

  pc.ontrack = e => {
    if (!peer.stream) {
      peer.stream = (e.streams && e.streams[0]) ? e.streams[0] : new MediaStream();
    }
    if (e.track && !peer.stream.getTracks().some(t => t.id === e.track.id)) {
      peer.stream.addTrack(e.track);
    }
    const videoEl = $(`video-${userId}`);
    const fallbackEl = $(`fallback-${userId}`);
    if (videoEl) {
      videoEl.srcObject = peer.stream;
      videoEl.play().catch(() => {});
      const vTrack = peer.stream.getVideoTracks()[0] || (e.track?.kind === 'video' ? e.track : null);
      if (vTrack) {
        if (fallbackEl) {
          const showFallback = !vTrack.enabled || vTrack.muted;
          fallbackEl.hidden = !showFallback;
          fallbackEl.style.display = showFallback ? 'flex' : 'none';
        }
        vTrack.onmute = () => {
          if (fallbackEl) {
            fallbackEl.hidden = false;
            fallbackEl.style.display = 'flex';
          }
        };
        vTrack.onunmute = () => {
          if (fallbackEl) {
            fallbackEl.hidden = true;
            fallbackEl.style.display = 'none';
          }
          videoEl.play().catch(() => {});
        };
      } else if (fallbackEl) {
        fallbackEl.hidden = false;
        fallbackEl.style.display = 'flex';
      }
    }
    const tileEl = $(`tile-${userId}`);
    if (tileEl) {
      peer.audioDetector = attachAudioDetector(peer.stream, tileEl);
    }
  };

  pc.onicecandidate = e => {
    if (e.candidate && callState.ws?.readyState === WebSocket.OPEN) {
      callState.ws.send(JSON.stringify({
        type: 1,
        target: 'SendIceCandidate',
        arguments: [callState.callId, userId, e.candidate.candidate, e.candidate.sdpMid || '', e.candidate.sdpMLineIndex || 0]
      }) + '\u001e');
    }
  };

  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'connected') {
      stopRingtone();
      startCallTimer();
      statusEl.textContent = 'Идёт групповой звонок';
    } else if (pc.connectionState === 'failed') {
      try {
        if (typeof pc.restartIce === 'function') pc.restartIce();
      } catch {}
    }
  };

  return peer;
}

async function flushQueuedCandidatesForPeer(peer) {
  if (!peer || !peer.pc || !peer.pc.remoteDescription) return;
  while (peer.queuedCandidates && peer.queuedCandidates.length > 0) {
    const cand = peer.queuedCandidates.shift();
    try {
      await peer.pc.addIceCandidate(new RTCIceCandidate(cand));
    } catch (e) {
      console.warn('Ошибка добавления ICE-кандидата для ' + peer.userId, e);
    }
  }
}

async function startLiveKitSession(callId) {
  if (!window.LivekitClient) {
    console.warn('LivekitClient SDK не найден в window, используется mesh-режим.');
    return false;
  }
  try {
    statusEl.textContent = 'Подключение к LiveKit SFU…';
    const resp = await fetch(`/api/messenger/calls/${encodeURIComponent(callId)}/token`, {
      method: 'POST',
      headers: {
        'Authorization': 'Bearer ' + callState.token,
        'Content-Type': 'application/json'
      }
    });
    if (!resp.ok) {
      console.warn('Сервер не вернул LiveKit токен, статус:', resp.status);
      return false;
    }
    const tokenData = await resp.json();
    const livekitUrl = tokenData.url;
    const livekitToken = tokenData.token;
    if (!livekitUrl || !livekitToken) return false;

    const { Room, RoomEvent, VideoPresets } = window.LivekitClient;
    const room = new Room({
      adaptiveStream: true,
      dynacast: true,
      videoCaptureDefaults: {
        resolution: VideoPresets.h720.resolution
      }
    });
    callState.livekitRoom = room;

    room.on(RoomEvent.TrackSubscribed, (track, publication, participant) => {
      let uId = 0;
      try {
        if (participant.metadata) uId = JSON.parse(participant.metadata).userId;
      } catch (e) {}
      if (!uId) uId = parseInt(participant.identity, 10) || 0;
      const fullName = participant.name || 'Участник';

      if (track.kind === 'video') {
        let tile = $(`tile-${uId}`);
        if (!tile) tile = addRemoteTileToGrid(uId, fullName);
        const videoEl = $(`video-${uId}`);
        const fallbackEl = $(`fallback-${uId}`);
        if (videoEl) {
          track.attach(videoEl);
          videoEl.style.display = 'block';
          if (fallbackEl) fallbackEl.hidden = true;
        }
      } else if (track.kind === 'audio') {
        const audioEl = track.attach();
        audioEl.id = 'livekit-audio-' + uId;
        document.body.appendChild(audioEl);
      }
    });

    room.on(RoomEvent.TrackUnsubscribed, (track, publication, participant) => {
      track.detach();
      let uId = 0;
      try {
        if (participant.metadata) uId = JSON.parse(participant.metadata).userId;
      } catch (e) {}
      if (!uId) uId = parseInt(participant.identity, 10) || 0;

      if (track.kind === 'video') {
        const videoEl = $(`video-${uId}`);
        const fallbackEl = $(`fallback-${uId}`);
        if (videoEl) videoEl.style.display = 'none';
        if (fallbackEl) fallbackEl.hidden = false;
      } else if (track.kind === 'audio') {
        const audioEl = document.getElementById('livekit-audio-' + uId);
        if (audioEl) audioEl.remove();
      }
    });

    room.on(RoomEvent.ActiveSpeakersChanged, (speakers) => {
      const activeIds = new Set();
      speakers.forEach(s => {
        let uId = 0;
        try {
          if (s.metadata) uId = JSON.parse(s.metadata).userId;
        } catch (e) {}
        if (!uId) uId = parseInt(s.identity, 10);
        if (uId) activeIds.add(uId);
      });

      document.querySelectorAll('.video-tile').forEach(tile => {
        const id = parseInt(tile.id.replace('tile-', ''), 10);
        if (id && activeIds.has(id)) {
          tile.classList.add('speaking');
        } else if (!tile.classList.contains('local-tile')) {
          tile.classList.remove('speaking');
        }
      });
    });

    room.on(RoomEvent.ParticipantConnected, (p) => {
      let uId = 0;
      try {
        if (p.metadata) uId = JSON.parse(p.metadata).userId;
      } catch (e) {}
      if (!uId) uId = parseInt(p.identity, 10) || 0;
      const fullName = p.name || 'Участник';
      addRemoteTileToGrid(uId, fullName);
      showToast(`${fullName} присоединился через SFU`);
    });

    room.on(RoomEvent.ParticipantDisconnected, (p) => {
      let uId = 0;
      try {
        if (p.metadata) uId = JSON.parse(p.metadata).userId;
      } catch (e) {}
      if (!uId) uId = parseInt(p.identity, 10) || 0;
      removeRemoteTileFromGrid(uId);
      const audioEl = document.getElementById('livekit-audio-' + uId);
      if (audioEl) audioEl.remove();
    });

    await room.connect(livekitUrl, livekitToken);
    callState.mode = 'livekit';
    statusEl.textContent = 'Подключено (LiveKit SFU)';
    startCallTimer();

    // Publish local stream tracks
    if (callState.localStream) {
      const vTrack = callState.localStream.getVideoTracks()[0];
      if (vTrack) {
        await room.localParticipant.publishTrack(vTrack, { simulcast: true });
      }
      const aTrack = callState.localStream.getAudioTracks()[0];
      if (aTrack) {
        await room.localParticipant.publishTrack(aTrack);
      }
    }
    return true;
  } catch (err) {
    console.error('Ошибка инициализации LiveKit SFU:', err);
    return false;
  }
}


// 7. Запись видеозвонка инициатором
function startRecording() {
  if (callState.isRecording || !callState.isCaller) return;

  try {
    // Микширование звука через Web Audio API (локальный микрофон + удаленный звук)
    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const dest = audioCtx.createMediaStreamDestination();

    if (callState.localStream && callState.localStream.getAudioTracks().length > 0) {
      const localAudioSource = audioCtx.createMediaStreamSource(callState.localStream);
      localAudioSource.connect(dest);
    }

    if (callState.remoteStream && callState.remoteStream.getAudioTracks().length > 0) {
      const remoteAudioSource = audioCtx.createMediaStreamSource(callState.remoteStream);
      remoteAudioSource.connect(dest);
    }

    // Собираем видео треки (удаленный экран/видео имеет приоритет, либо локальное видео)
    const videoTracks = [];
    if (callState.remoteStream && callState.remoteStream.getVideoTracks().length > 0) {
      videoTracks.push(callState.remoteStream.getVideoTracks()[0]);
    } else if (callState.localStream && callState.localStream.getVideoTracks().length > 0) {
      videoTracks.push(callState.localStream.getVideoTracks()[0]);
    }

    const mixedAudioTracks = dest.stream.getAudioTracks();
    const tracksToRecord = [...videoTracks, ...mixedAudioTracks];

    if (tracksToRecord.length === 0) {
      showToast('Нет активных медиапотоков для записи');
      return;
    }

    const recordStream = new MediaStream(tracksToRecord);
    callState.recordChunks = [];

    let mimeType = 'video/webm;codecs=vp9,opus';
    if (!MediaRecorder.isTypeSupported(mimeType)) {
      mimeType = 'video/webm';
    }

    const recorder = new MediaRecorder(recordStream, { mimeType });
    recorder.ondataavailable = e => {
      if (e.data && e.data.size > 0) {
        callState.recordChunks.push(e.data);
      }
    };

    recorder.onstop = async () => {
      await saveRecordingFile();
    };

    recorder.start(1000); // Чанки каждую секунду
    callState.recorder = recorder;
    callState.isRecording = true;

    // UI: кнопка записи и индикатор REC
    btnRecord.classList.add('active');
    $('btn-record-label').textContent = 'Стоп';
    recordIndicator.hidden = false;

    callState.recordStartTime = Date.now();
    clearInterval(callState.recordInterval);
    callState.recordInterval = setInterval(() => {
      const elapsed = Math.floor((Date.now() - callState.recordStartTime) / 1000);
      const m = String(Math.floor(elapsed / 60)).padStart(2, '0');
      const s = String(elapsed % 60).padStart(2, '0');
      recordTimerEl.textContent = `REC ${m}:${s}`;
    }, 1000);

    showToast('Идёт запись видеозвонка…');
  } catch (err) {
    showToast('Не удалось запустить запись: ' + err.message);
  }
}

function stopRecording() {
  if (!callState.isRecording || !callState.recorder) return;
  try {
    callState.isRecording = false;
    clearInterval(callState.recordInterval);
    recordIndicator.hidden = true;
    btnRecord.classList.remove('active');
    $('btn-record-label').textContent = 'Запись';

    if (callState.recorder.state !== 'inactive') {
      callState.recorder.stop();
    }
  } catch (err) {
    console.error(err);
  }
}

async function saveRecordingFile() {
  if (callState.recordChunks.length === 0) return;
  const blob = new Blob(callState.recordChunks, { type: 'video/webm' });
  callState.recordChunks = [];

  const now = new Date();
  const dateStr = now.toISOString().replace(/T/, '_').replace(/:/g, '-').slice(0, 19);
  const cleanTarget = (callState.targetName || 'Звонок').replace(/[^a-zA-Z0-9а-яА-ЯёЁ_\- ]/g, '').trim();
  const filename = `Видеозвонок_${cleanTarget}_${dateStr}.webm`;

  let savedDirectly = false;

  // 1. Попытка сохранить напрямую в папку «Видео» через локальный C# приемник клиента
  try {
    const saveUrl = `http://127.0.0.1:${callState.localPort}/api/save-call-recording?name=${encodeURIComponent(filename)}`;
    const res = await fetch(saveUrl, {
      method: 'POST',
      body: blob,
      headers: {
        'Content-Type': 'video/webm',
        'X-Filename': encodeURIComponent(filename)
      }
    });

    if (res.ok) {
      savedDirectly = true;
      showToast(`Запись сохранена в папку «Видео»:\n${filename}`, 5000);
    }
  } catch {
    savedDirectly = false;
  }

  // 2. Fallback: если локальный C# приемник недоступен (чистый браузер)
  if (!savedDirectly) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.style.display = 'none';
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }, 2000);
    showToast(`Запись скачана в папку «Видео / Загрузки»:\n${filename}`, 5000);
  }
}

// 8. Завершение вызова
function hangupCall() {
  if (callState.isRecording) {
    stopRecording();
  }

  stopRingtone();
  clearTimeout(callState.callTimeout);
  clearInterval(callState.timerInterval);

  if (callState.callId && callState.ws?.readyState === WebSocket.OPEN) {
    if (callState.isGroup) {
      callState.ws.send(JSON.stringify({ type: 1, target: 'LeaveGroupCall', arguments: [callState.callId] }) + '\u001e');
    } else {
      callState.ws.send(JSON.stringify({ type: 1, target: 'EndCall', arguments: [callState.callId, callState.peerId || 0] }) + '\u001e');
    }
  }

  if (callState.livekitRoom) {
    try {
      callState.livekitRoom.disconnect();
    } catch (e) {}
    callState.livekitRoom = null;
  }

  // Закрываем все соединения участников в группе
  callState.peers.forEach(p => {
    if (p.audioDetector?.interval) clearInterval(p.audioDetector.interval);
    if (p.audioDetector?.ctx) p.audioDetector.ctx.close().catch(()=>{});
    if (p.pc) p.pc.close();
  });
  callState.peers.clear();

  if (callState.pc) {
    callState.pc.close();
    callState.pc = null;
  }

  if (callState.localStream) {
    callState.localStream.getTracks().forEach(t => t.stop());
  }

  statusEl.textContent = 'Звонок завершён';

  // Закрываем окно через 800 мс
  setTimeout(() => {
    window.close();
  }, 800);
}

// 9. Обработчики кнопок
btnHangup.onclick = hangupCall;

// --- Device Settings (Microphone selection & test meter) ---
let modalMeterAudioCtx = null;
let modalMeterAnimId = null;

function stopModalMeter() {
  if (modalMeterAnimId) { cancelAnimationFrame(modalMeterAnimId); modalMeterAnimId = null; }
  if (modalMeterAudioCtx) { modalMeterAudioCtx.close().catch(()=>{}); modalMeterAudioCtx = null; }
  if (callMicMeterFill) callMicMeterFill.style.width = '0%';
}

function startModalMeter() {
  stopModalMeter();
  if (!callState.localStream) return;
  const audioTracks = callState.localStream.getAudioTracks();
  if (!audioTracks.length) return;
  try {
    modalMeterAudioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const source = modalMeterAudioCtx.createMediaStreamSource(new MediaStream([audioTracks[0]]));
    const analyser = modalMeterAudioCtx.createAnalyser();
    analyser.fftSize = 128;
    source.connect(analyser);
    const buffer = new Uint8Array(analyser.frequencyBinCount);

    function update() {
      if (!modalMeterAudioCtx) return;
      analyser.getByteFrequencyData(buffer);
      let sum = 0;
      for (let i = 0; i < buffer.length; i++) sum += buffer[i];
      const avg = sum / buffer.length;
      const pct = Math.min(100, Math.round((avg / 128) * 100 * 1.5));
      if (callMicMeterFill) callMicMeterFill.style.width = pct + '%';
      modalMeterAnimId = requestAnimationFrame(update);
    }
    update();
  } catch (e) {
    console.warn('Meter error:', e);
  }
}

async function openDeviceModal() {
  if (deviceModal) deviceModal.showModal();
  startModalMeter();
  try {
    const audioInputs = await HelperMedia.inputs();
    callMicSelect.replaceChildren();

    const defOption = document.createElement('option');
    defOption.value = '';
    defOption.textContent = 'По умолчанию (системный)';
    callMicSelect.append(defOption);

    const activeTrack = callState.localStream?.getAudioTracks()[0];
    const activeSettings = activeTrack && activeTrack.getSettings ? activeTrack.getSettings() : {};
    const currentDevId = activeSettings.deviceId || callState.activeMicDeviceId || localStorage.getItem('helper.chat.mic_id') || '';

    audioInputs.forEach((d, idx) => {
      const opt = document.createElement('option');
      opt.value = d.deviceId;
      opt.textContent = '🎤 ' + (d.label || `Микрофон ${idx + 1}`);
      callMicSelect.append(opt);
    });

    callMicSelect.value = currentDevId;
  } catch (err) {
    showToast(HelperMedia.explain(err, 'Микрофон'));
  }
}

async function switchMicrophone(deviceId) {
  try {
    const constraints = {
      audio: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true
      }
    };
    if (deviceId) {
      constraints.audio.deviceId = { exact: deviceId };
    }
    const newStream = await navigator.mediaDevices.getUserMedia(constraints);
    const newTrack = newStream.getAudioTracks()[0];
    if (!newTrack) throw new Error('Не удалось получить аудиодорожку');

    // 1. Заменяем трек в localStream
    if (callState.localStream) {
      const oldTrack = callState.localStream.getAudioTracks()[0];
      if (oldTrack) {
        callState.localStream.removeTrack(oldTrack);
        oldTrack.stop();
      }
      callState.localStream.addTrack(newTrack);
    }

    // 2. Заменяем трек в 1-на-1 WebRTC RTCPeerConnection
    if (callState.pc) {
      const senders = callState.pc.getSenders();
      const audSender = senders.find(s => s.track && s.track.kind === 'audio');
      if (audSender) {
        await audSender.replaceTrack(newTrack);
      }
    }

    // 3. Заменяем трек во всех групповых mesh peer connections
    callState.peers.forEach(p => {
      if (p.pc) {
        const senders = p.pc.getSenders();
        const audSender = senders.find(s => s.track && s.track.kind === 'audio');
        if (audSender) {
          audSender.replaceTrack(newTrack).catch(() => {});
        }
      }
    });

    // 4. Заменяем микрофон в LiveKit SFU (если активно)
    if (callState.livekitRoom) {
      callState.livekitRoom.switchActiveDevice('audioinput', deviceId).catch(() => {});
    }

    showToast('Микрофон переключен: ' + (newTrack.label || 'Выбранное устройство'));
  } catch (err) {
    showToast('Ошибка переключения микрофона: ' + err.message);
  }
}

if (btnDeviceSettings) btnDeviceSettings.onclick = openDeviceModal;
if (btnCloseDeviceModal) btnCloseDeviceModal.onclick = () => { stopModalMeter(); deviceModal.close(); };
if (deviceModal) deviceModal.addEventListener('close', () => { stopModalMeter(); });

if (callMicSelect) {
  callMicSelect.onchange = async () => {
    const chosenId = callMicSelect.value;
    localStorage.setItem('helper.chat.mic_id', chosenId);
    callState.activeMicDeviceId = chosenId;
    await switchMicrophone(chosenId);
    startModalMeter();
  };
}

btnMic.onclick = () => {
  if (!callState.localStream) return;
  const audioTrack = callState.localStream.getAudioTracks()[0];
  if (audioTrack) {
    audioTrack.enabled = !audioTrack.enabled;
    btnMic.querySelector('.icon').textContent = audioTrack.enabled ? '🎤' : '🔇';
    btnMic.classList.toggle('muted', !audioTrack.enabled);
    const localMic = $('local-tile-mic');
    if (localMic) localMic.textContent = audioTrack.enabled ? '🎤' : '🔇';
    if (callState.livekitRoom) {
      callState.livekitRoom.localParticipant.setMicrophoneEnabled(audioTrack.enabled).catch(() => {});
    }
  }
};

btnCam.onclick = () => {
  if (!callState.localStream) return;
  const videoTrack = callState.localStream.getVideoTracks()[0];
  if (videoTrack) {
    videoTrack.enabled = !videoTrack.enabled;
    btnCam.querySelector('.icon').textContent = videoTrack.enabled ? '📷' : '🚫';
    btnCam.classList.toggle('muted', !videoTrack.enabled);
    $('local-pip-container').style.opacity = videoTrack.enabled ? '1' : '0.4';
    const localCam = $('local-tile-cam');
    if (localCam) localCam.textContent = videoTrack.enabled ? '📷' : '🚫';
    const localFallback = $('local-grid-fallback');
    if (localFallback) localFallback.hidden = videoTrack.enabled;
    if (callState.livekitRoom) {
      callState.livekitRoom.localParticipant.setCameraEnabled(videoTrack.enabled).catch(() => {});
    }
  }
};

function sendSignalRPacket(target, args) {
  if (callState.ws?.readyState === WebSocket.OPEN) {
    callState.ws.send(JSON.stringify({
      type: 1,
      target,
      arguments: args
    }) + '\u001e');
  }
}

async function stopScreenShare() {
  if (!callState.isScreenSharing) return;
  callState.isScreenSharing = false;
  btnScreen.classList.remove('muted');

  if (callState.screenStream) {
    callState.screenStream.getTracks().forEach(t => { try { t.stop(); } catch {} });
    callState.screenStream = null;
  }

  const origTrack = callState.localStream?.getVideoTracks()[0] || null;

  if (callState.pc) {
    const s = findVideoSender(callState.pc);
    if (s) {
      try { await s.replaceTrack(origTrack); } catch (e) { console.warn('replaceTrack 1:1 error', e); }
    }
  }

  callState.peers.forEach(p => {
    if (p.pc) {
      const s = findVideoSender(p.pc);
      if (s) {
        try { s.replaceTrack(origTrack); } catch (e) { console.warn('replaceTrack peer error', e); }
      }
    }
  });

  if (callState.livekitRoom) {
    try {
      if (callState.livekitScreenPublication) {
        await callState.livekitRoom.localParticipant.unpublishTrack(callState.livekitScreenPublication.track);
        callState.livekitScreenPublication = null;
      } else {
        await callState.livekitRoom.localParticipant.setScreenShareEnabled(false);
      }
    } catch (e) {
      console.warn('LiveKit unpublish screen error', e);
    }
  }

  sendSignalRPacket('SetScreenSharing', [callState.callId, false]);
  showToast('Демонстрация экрана остановлена');
}

btnScreen.onclick = async () => {
  if (callState.isScreenSharing) {
    await stopScreenShare();
    return;
  }

  try {
    const screenStream = await navigator.mediaDevices.getDisplayMedia({
      video: { frameRate: 15 },
      audio: false
    });
    const screenTrack = screenStream.getVideoTracks()[0];
    if (!screenTrack) return;
    screenTrack.contentHint = 'detail';
    callState.screenStream = screenStream;
    callState.isScreenSharing = true;

    // 1-на-1
    if (callState.pc) {
      let vidSender = findVideoSender(callState.pc);
      let needsRenegotiate = false;
      if (typeof callState.pc.getTransceivers === 'function') {
        const tr = callState.pc.getTransceivers().find(t =>
          (t.sender && (t.sender === vidSender || t.sender.track?.kind === 'video')) ||
          (t.receiver && t.receiver.track?.kind === 'video')
        );
        if (tr) {
          if (tr.direction !== 'sendrecv') {
            tr.direction = 'sendrecv';
            needsRenegotiate = true;
          }
          if (tr.sender) {
            try {
              await tr.sender.replaceTrack(screenTrack);
            } catch (e) {
              console.warn('replaceTrack on transceiver error:', e);
            }
          }
        }
      }
      if (!vidSender) {
        callState.pc.addTrack(screenTrack, screenStream);
        needsRenegotiate = true;
      }
      if (needsRenegotiate) {
        await renegotiateCall(callState.pc, callState.peerId);
      }
    }

    // Группа mesh
    for (const [peerUserId, p] of callState.peers.entries()) {
      if (p.pc) {
        let vidSender = findVideoSender(p.pc);
        let needsRenegotiate = false;
        if (typeof p.pc.getTransceivers === 'function') {
          const tr = p.pc.getTransceivers().find(t =>
            (t.sender && (t.sender === vidSender || t.sender.track?.kind === 'video')) ||
            (t.receiver && t.receiver.track?.kind === 'video')
          );
          if (tr) {
            if (tr.direction !== 'sendrecv') {
              tr.direction = 'sendrecv';
              needsRenegotiate = true;
            }
            if (tr.sender) {
              try { await tr.sender.replaceTrack(screenTrack); } catch (e) {}
            }
          }
        }
        if (!vidSender) {
          p.pc.addTrack(screenTrack, screenStream);
          needsRenegotiate = true;
        }
        if (needsRenegotiate) {
          await renegotiateCall(p.pc, peerUserId);
        }
      }
    }

    // LiveKit SFU (избегаем второго окна запроса экрана)
    if (callState.livekitRoom) {
      try {
        const pub = await callState.livekitRoom.localParticipant.publishTrack(screenTrack, {
          source: 'screen_share',
          name: 'screen'
        });
        callState.livekitScreenPublication = pub;
      } catch (e) {
        console.warn('LiveKit screen publish error:', e);
      }
    }

    btnScreen.classList.add('muted');
    sendSignalRPacket('SetScreenSharing', [callState.callId, true]);
    showToast('Демонстрация экрана запущена');

    screenTrack.onended = () => {
      stopScreenShare();
    };
  } catch (err) {
    console.warn('DisplayMedia cancel/error:', err);
    callState.isScreenSharing = false;
    btnScreen.classList.remove('muted');
    showToast('Демонстрация экрана отменена');
  }
};

// Модальное окно приглашения участников
let inviteSearchTimer = null;

async function loadInviteCandidates(query = '') {
  if (!inviteCandidatesList) return;
  inviteCandidatesList.innerHTML = '<div class="loading-state">Загрузка кандидатов…</div>';

  try {
    const qParam = query.trim() ? `&q=${encodeURIComponent(query.trim())}` : '';
    const res = await fetch(`/api/messenger/calls/${encodeURIComponent(callState.callId)}/invite-candidates?${qParam}`, {
      headers: {
        'Authorization': 'Bearer ' + callState.token,
        'X-Messenger-Web': '1'
      }
    });

    if (!res.ok) {
      inviteCandidatesList.innerHTML = '<div class="empty-state">Не удалось загрузить пользователей</div>';
      return;
    }

    const candidates = await res.json();
    if (!candidates || candidates.length === 0) {
      inviteCandidatesList.innerHTML = '<div class="empty-state">Пользователи не найдены</div>';
      return;
    }

    inviteCandidatesList.replaceChildren();
    candidates.forEach(u => {
      const row = document.createElement('div');
      row.className = 'candidate-row';
      row.innerHTML = `
        <div class="candidate-avatar">${escapeHtml(getInitials(u.fullName))}</div>
        <div class="candidate-info">
          <span class="candidate-name">${escapeHtml(u.fullName)}</span>
          <span class="candidate-login">@${escapeHtml(u.username)}</span>
        </div>
        <button type="button" class="btn-candidate-invite" data-id="${u.id}">Пригласить</button>
      `;
      const btn = row.querySelector('.btn-candidate-invite');
      btn.onclick = () => inviteUserToCall(u.id, u.fullName, btn);
      inviteCandidatesList.appendChild(row);
    });
  } catch (err) {
    inviteCandidatesList.innerHTML = `<div class="empty-state">Ошибка: ${escapeHtml(err.message)}</div>`;
  }
}

async function inviteUserToCall(targetUserId, targetFullName, btnElement) {
  if (!btnElement) return;
  btnElement.disabled = true;
  btnElement.textContent = 'Вызываем…';

  try {
    sendSignalRPacket('InviteToCall', [callState.callId, targetUserId]);
    btnElement.textContent = 'Приглашён ✓';
    showToast(`Вызов отправлен пользователю ${targetFullName}`);
  } catch (err) {
    btnElement.disabled = false;
    btnElement.textContent = 'Пригласить';
    showToast(`Ошибка приглашения: ${err.message}`);
  }
}

function openInviteModal() {
  if (inviteModal) {
    if (inviteSearchInput) inviteSearchInput.value = '';
    inviteModal.showModal();
    loadInviteCandidates('');
  }
}

if (btnAddParticipant) {
  btnAddParticipant.onclick = openInviteModal;
}
if (btnInviteFromRoster) {
  btnInviteFromRoster.onclick = () => {
    if (rosterModal) rosterModal.close();
    openInviteModal();
  };
}
if (btnCloseInviteModal) {
  btnCloseInviteModal.onclick = () => {
    if (inviteModal) inviteModal.close();
  };
}
if (inviteSearchInput) {
  inviteSearchInput.oninput = () => {
    clearTimeout(inviteSearchTimer);
    inviteSearchTimer = setTimeout(() => {
      loadInviteCandidates(inviteSearchInput.value);
    }, 300);
  };
}

// Поднять / опустить руку
if (btnRaiseHand) {
  btnRaiseHand.onclick = () => {
    callState.myHandRaised = !callState.myHandRaised;
    btnRaiseHand.classList.toggle('active-hand', callState.myHandRaised);
    const localHand = $('local-tile-hand');
    if (localHand) localHand.hidden = !callState.myHandRaised;

    if (callState.ws?.readyState === WebSocket.OPEN) {
      callState.ws.send(JSON.stringify({
        type: 1,
        target: 'RaiseHand',
        arguments: [callState.callId, callState.myHandRaised]
      }) + '\u001e');
    }
  };
}

// Открытие модального окна участников
if (btnParticipants) {
  btnParticipants.onclick = () => {
    updateRosterModal();
    if (rosterModal) rosterModal.showModal();
  };
}
if (btnCloseRosterModal) {
  btnCloseRosterModal.onclick = () => {
    if (rosterModal) rosterModal.close();
  };
}

// Завершить групповой звонок для всех (только для организатора)
if (btnEndForAll) {
  btnEndForAll.onclick = () => {
    if (confirm('Вы уверены, что хотите завершить групповой звонок для всех участников?')) {
      if (callState.ws?.readyState === WebSocket.OPEN) {
        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'EndGroupCallForAll',
          arguments: [callState.callId]
        }) + '\u001e');
      }
      hangupCall();
    }
  };
}

btnRecord.onclick = async () => {
  if (callState.isGroup) {
    const isNowRec = !callState.isRecording;
    const action = isNowRec ? 'start' : 'stop';
    try {
      const res = await fetch(`/api/messenger/calls/${encodeURIComponent(callState.callId)}/recording/${action}`, {
        method: 'POST',
        headers: {
          'Authorization': 'Bearer ' + callState.token,
          'Content-Type': 'application/json'
        }
      });
      if (res.ok) {
        showToast(isNowRec ? '🔴 Запрос на запись звонка отправлен' : '⏹️ Запись остановлена', 3000);
      } else {
        if (callState.isRecording) stopRecording(); else startRecording();
      }
    } catch (e) {
      if (callState.isRecording) stopRecording(); else startRecording();
    }
  } else {
    if (callState.isRecording) {
      stopRecording();
    } else {
      startRecording();
    }
  }
};

// 10. SignalR подключение
function unpackArg(args, index, prop) {
  if (!args || args.length === 0) return undefined;
  if (typeof args[0] === 'object' && args[0] !== null && !Array.isArray(args[0])) {
    return prop ? args[0][prop] : args[0];
  }
  return args[index];
}

async function ensureAuthenticated() {
  if (callState.token) return;
  try {
    const ssoRes = await fetch('/api/messenger/web/windows', {
      method: 'GET',
      credentials: 'same-origin',
      headers: { 'X-Messenger-Web': '1' }
    });
    if (ssoRes.ok) {
      const data = await ssoRes.json();
      if (data && data.id && !callState.currentUserId) {
        callState.currentUserId = data.id;
      }
    }
  } catch (e) {
    console.warn('SSO fallback failed:', e);
  }
}

async function startSignalR() {
  try {
    statusEl.textContent = 'Авторизация…';
    await ensureAuthenticated();

    const headers = { 'X-Messenger-Web': '1' };
    if (callState.token) {
      headers['Authorization'] = 'Bearer ' + callState.token;
    }
    let negotiateUrl = '/messengerHub/negotiate?negotiateVersion=1';
    if (callState.token) {
      negotiateUrl += '&access_token=' + encodeURIComponent(callState.token);
    }

    const response = await fetch(negotiateUrl, {
      method: 'POST',
      credentials: 'same-origin',
      headers
    });

    if (!response.ok) {
      throw new Error('ошибка авторизации в сервисе звонков (HTTP ' + response.status + ')');
    }
    const negotiation = await response.json();
    let wsUrl = location.origin.replace(/^http/, 'ws') + '/messengerHub?id=' + encodeURIComponent(negotiation.connectionToken);
    if (callState.token) {
      wsUrl += '&access_token=' + encodeURIComponent(callState.token);
    }
    const ws = new WebSocket(wsUrl);
    callState.ws = ws;

    let handshake = false;
    let buffer = '';

    ws.onopen = () => {
      ws.send('{"protocol":"json","version":1}\u001e');
    };

    ws.onmessage = async e => {
      buffer += e.data;
      let split;
      while ((split = buffer.indexOf('\u001e')) >= 0) {
        const raw = buffer.slice(0, split);
        buffer = buffer.slice(split + 1);
        if (!raw) continue;
        const packet = JSON.parse(raw);

        if (!handshake) {
          handshake = true;
          // Инициализация звонка после рукопожатия
          onConnected();
        } else if (packet.type === 1) {
          handleSignalRPacket(packet);
        }
      }
    };

    ws.onclose = () => {
      statusEl.textContent = 'Соединение потеряно';
    };
  } catch (err) {
    statusEl.textContent = 'Ошибка подключения: ' + err.message;
  }
}

async function onConnected() {
  try {
    await acquireLocalMedia();

    if (callState.isGroup) {
      addLocalTileToGrid();
      if (callState.isCaller) {
        statusEl.textContent = 'Ожидание участников…';
        playRingtone();
        clearTimeout(callState.callTimeout);
        callState.callTimeout = setTimeout(() => {
          if (callState.peers.size === 0) {
            statusEl.textContent = 'Никто не ответил';
            setTimeout(() => hangupCall(), 2500);
          }
        }, 60000);

        callState.roster = [{
          userId: callState.currentUserId,
          fullName: 'Вы',
          isCaller: true,
          handRaised: false
        }];
        updateRosterModal();

        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'StartGroupCall',
          arguments: [callState.groupId, callState.isVideo, callState.callId]
        }) + '\u001e');
      } else {
        statusEl.textContent = 'Подключение к звонку…';
        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'JoinGroupCall',
          arguments: [callState.callId]
        }) + '\u001e');
      }
    } else {
      if (callState.isCaller) {
        statusEl.textContent = 'Вызов…';
        playRingtone();
        clearTimeout(callState.callTimeout);
        callState.callTimeout = setTimeout(() => {
          if (!callState.startTime) {
            statusEl.textContent = 'Абонент не отвечает';
            setTimeout(() => hangupCall(), 2500);
          }
        }, 45000);

        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'StartCall',
          arguments: [callState.peerId, callState.isVideo, callState.callId]
        }) + '\u001e');
      } else {
        statusEl.textContent = 'Подключение к вызову…';
        if (callState.callId) {
          callState.ws.send(JSON.stringify({
            type: 1,
            target: 'AcceptCall',
            arguments: [callState.callId, callState.peerId]
          }) + '\u001e');
        }
      }
    }
  } catch (err) {
    statusEl.textContent = err.message;
  }
}

async function handleSignalRPacket(packet) {
  const packetCallId = unpackArg(packet.arguments, 0, 'callId');
  if (packetCallId && callState.callId && packetCallId !== callState.callId) {
    return;
  }

  if (packet.target === 'GroupCallJoined') {
    clearTimeout(callState.callTimeout);
    stopRingtone();
    const data = unpackArg(packet.arguments, 1) || {};
    const participants = data.participants || [];
    callState.roster = participants;
    callState.mode = data.mode || (participants.length > 4 ? 'livekit' : 'mesh');
    updateRosterModal();

    if (data.isRecording) {
      callState.isRecording = true;
      recordIndicator.hidden = false;
      recordTimerEl.textContent = `REC (${data.recordingStartedBy || 'Запись'})`;
    }

    if (callState.mode === 'livekit' || participants.length > 4) {
      const livekitStarted = await startLiveKitSession(callState.callId);
      if (livekitStarted) return;
    }

    statusEl.textContent = 'Подключение к участникам…';

    for (const p of participants) {
      if (p.userId !== callState.currentUserId) {
        try {
          const peer = await setupPeerConnectionForUser(p.userId, p.fullName, true);
          if (p.handRaised) {
            const tileHand = $('tile-hand-' + p.userId);
            if (tileHand) tileHand.hidden = false;
          }
          const offer = await peer.pc.createOffer();
          await peer.pc.setLocalDescription(offer);
          callState.ws.send(JSON.stringify({
            type: 1,
            target: 'SendSdpOffer',
            arguments: [callState.callId, p.userId, offer.sdp]
          }) + '\u001e');
        } catch (e) {
          console.error('Ошибка инициализации peer connection к ' + p.userId, e);
        }
      }
    }
    return;
  }

  if (packet.target === 'CallUpgradedToGroup') {
    const data = unpackArg(packet.arguments, 1) || {};
    const participants = data.participants || [];
    callState.isGroup = true;
    callState.roster = participants;
    updateRosterModal();

    btnRaiseHand.hidden = false;
    btnParticipants.hidden = false;
    videoContainer.hidden = true;
    groupGrid.hidden = false;

    // Переносим существующий 1:1 pc в сетку без разрыва связи!
    addLocalTileToGrid();
    if (callState.pc) {
      const remoteUser = participants.find(p => p.userId !== callState.currentUserId) || {
        userId: callState.peerId,
        fullName: callState.targetName
      };
      const remoteId = remoteUser.userId;
      const remoteName = remoteUser.fullName || callState.targetName;

      addRemoteTileToGrid(remoteId, remoteName);
      const videoEl = $(`video-${remoteId}`);
      const fallbackEl = $(`fallback-${remoteId}`);
      if (videoEl && callState.remoteStream) {
        videoEl.srcObject = callState.remoteStream;
        const vTrack = callState.remoteStream.getVideoTracks()[0];
        if (fallbackEl) fallbackEl.hidden = !(vTrack && vTrack.enabled && !vTrack.muted);
      }
      const tileEl = $(`tile-${remoteId}`);
      let detector = null;
      if (tileEl && callState.remoteStream) {
        detector = attachAudioDetector(callState.remoteStream, tileEl);
      }

      callState.peers.set(remoteId, {
        userId: remoteId,
        fullName: remoteName,
        pc: callState.pc,
        stream: callState.remoteStream,
        queuedCandidates: [],
        audioDetector: detector
      });
      callState.pc = null;
    }

    showToast('Звонок переведён в групповой режим');
    return;
  }

  if (packet.target === 'ScreenShareChanged') {
    const fromUserId = unpackArg(packet.arguments, 1);
    const isSharing = !!unpackArg(packet.arguments, 2);
    const presenterName = unpackArg(packet.arguments, 3) || 'Собеседник';
    callState.remoteScreenSharing = isSharing;

    if (!callState.isGroup) {
      if (isSharing) {
        statusEl.textContent = `🖥 ${presenterName} показывает экран`;
        showToast(`🖥 ${presenterName} запустил(а) показ экрана`);
        ensureRemoteVideoAttached();
        remoteVideo.style.objectFit = 'contain';
        setRemoteAvatarFallbackVisible(false);
      } else {
        statusEl.textContent = 'Идёт разговор';
        remoteVideo.style.objectFit = 'cover';
        const vTrack = callState.remoteStream?.getVideoTracks()[0];
        const hasCamera = !!(vTrack && vTrack.enabled && !vTrack.muted);
        setRemoteAvatarFallbackVisible(!hasCamera);
        showToast('Демонстрация экрана завершена');
      }
    } else {
      const tile = $(`tile-${fromUserId}`);
      const fallback = $(`fallback-${fromUserId}`);
      const videoEl = $(`video-${fromUserId}`);
      const peer = callState.peers.get(fromUserId);
      if (tile) {
        tile.classList.toggle('presenter-tile', isSharing);
      }
      if (fallback) {
        fallback.hidden = isSharing;
        fallback.style.display = isSharing ? 'none' : 'flex';
      }
      if (videoEl && isSharing) {
        videoEl.style.objectFit = 'contain';
        if (peer && peer.pc) {
          if (!peer.stream) peer.stream = new MediaStream();
          const receivers = typeof peer.pc.getReceivers === 'function' ? peer.pc.getReceivers() : [];
          receivers.forEach(r => {
            if (r.track && !peer.stream.getTracks().some(t => t.id === r.track.id)) {
              peer.stream.addTrack(r.track);
            }
          });
          videoEl.srcObject = peer.stream;
          videoEl.play().catch(() => {});
        }
      } else if (videoEl) {
        videoEl.style.objectFit = 'cover';
      }
      showToast(isSharing ? `🖥 ${presenterName} показывает экран` : `Демонстрация экрана завершена (${presenterName})`);
    }
    return;
  }

  if (packet.target === 'CallRecordingStateChanged') {
    const isRec = !!unpackArg(packet.arguments, 1);
    const startedBy = unpackArg(packet.arguments, 2) || '';
    callState.isRecording = isRec;
    if (isRec) {
      recordIndicator.hidden = false;
      recordTimerEl.textContent = `REC (${startedBy || 'Запись'})`;
      showToast(`🔴 Идёт запись звонка (${startedBy || 'Организатор'})`, 4000);
    } else {
      recordIndicator.hidden = true;
      showToast('⏹️ Запись звонка остановлена', 3000);
    }
    return;
  }

  if (packet.target === 'ParticipantJoined') {
    const userObj = unpackArg(packet.arguments, 1) || {};
    const userId = userObj.userId;
    const fullName = userObj.fullName || 'Новый участник';
    if (!userId || userId === callState.currentUserId) return;

    if (!callState.roster.some(r => r.userId === userId)) {
      callState.roster.push({
        userId,
        fullName,
        username: userObj.username,
        isCaller: false,
        handRaised: false
      });
      updateRosterModal();
    }
    showToast(`${fullName} присоединился к звонку`);
    addRemoteTileToGrid(userId, fullName);
    return;
  }

  if (packet.target === 'ParticipantLeft') {
    const userObj = unpackArg(packet.arguments, 1) || {};
    const userId = userObj.userId || userObj;
    const fullName = userObj.fullName || 'Участник';

    showToast(`${fullName} покинул звонок`);
    removeRemoteTileFromGrid(userId);

    const peer = callState.peers.get(userId);
    if (peer) {
      if (peer.audioDetector?.interval) clearInterval(peer.audioDetector.interval);
      if (peer.audioDetector?.ctx) peer.audioDetector.ctx.close().catch(()=>{});
      if (peer.pc) peer.pc.close();
      callState.peers.delete(userId);
    }

    callState.roster = callState.roster.filter(p => p.userId !== userId);
    updateRosterModal();
    return;
  }

  if (packet.target === 'ParticipantHandChanged') {
    const userId = unpackArg(packet.arguments, 1);
    const handRaised = !!unpackArg(packet.arguments, 2);

    const rItem = callState.roster.find(p => p.userId === userId);
    if (rItem) rItem.handRaised = handRaised;

    const tileHand = $('tile-hand-' + userId);
    if (tileHand) tileHand.hidden = !handRaised;

    if (userId === callState.currentUserId) {
      callState.myHandRaised = handRaised;
      if (btnRaiseHand) btnRaiseHand.classList.toggle('active-hand', handRaised);
      const localHand = $('local-tile-hand');
      if (localHand) localHand.hidden = !handRaised;
    }

    updateRosterModal();

    const name = rItem ? rItem.fullName : (userId === callState.currentUserId ? 'Вы' : 'Участник');
    showToast(handRaised ? `✋ ${name} поднял(а) руку` : `${name} опустил(а) руку`);
    return;
  }

  if (packet.target === 'CallAccepted') {
    clearTimeout(callState.callTimeout);
    const callId = packetCallId || callState.callId;
    const byUserId = unpackArg(packet.arguments, 1, 'userId') || unpackArg(packet.arguments, 1, 'callerId') || unpackArg(packet.arguments, 1) || callState.peerId;
    stopRingtone();
    statusEl.textContent = 'Соединение…';
    if (callState.isCaller) {
      const pc = await setupPeerConnection(callId, byUserId);
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      callState.ws.send(JSON.stringify({
        type: 1,
        target: 'SendSdpOffer',
        arguments: [callId, byUserId, offer.sdp]
      }) + '\u001e');
    }
    return;
  }

  if (packet.target === 'SdpOfferReceived') {
    const callId = packetCallId || callState.callId;
    const fromUserId = unpackArg(packet.arguments, 1, 'senderId') || unpackArg(packet.arguments, 1, 'userId') || unpackArg(packet.arguments, 1) || callState.peerId;
    const sdp = unpackArg(packet.arguments, 2, 'sdp') || packet.arguments[2];

    stopRingtone();

    if (callState.isGroup) {
      const rosterUser = callState.roster.find(r => r.userId === fromUserId);
      const fullName = rosterUser ? rosterUser.fullName : 'Участник';
      let peer = callState.peers.get(fromUserId);
      if (!peer || !peer.pc) {
        peer = await setupPeerConnectionForUser(fromUserId, fullName, false);
      }
      await peer.pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp }));
      await flushQueuedCandidatesForPeer(peer);
      const answer = await peer.pc.createAnswer();
      await peer.pc.setLocalDescription(answer);
      callState.ws.send(JSON.stringify({
        type: 1,
        target: 'SendSdpAnswer',
        arguments: [callId, fromUserId, answer.sdp]
      }) + '\u001e');
      return;
    }

    let pc = callState.pc;
    if (!pc) pc = await setupPeerConnection(callId, fromUserId);
    await pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp }));
    await flushQueuedCandidates();
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    callState.ws.send(JSON.stringify({
      type: 1,
      target: 'SendSdpAnswer',
      arguments: [callId, fromUserId, answer.sdp]
    }) + '\u001e');
    return;
  }

  if (packet.target === 'SdpAnswerReceived') {
    const fromUserId = unpackArg(packet.arguments, 1, 'senderId') || unpackArg(packet.arguments, 1, 'userId') || unpackArg(packet.arguments, 1) || callState.peerId;
    const sdp = unpackArg(packet.arguments, 2, 'sdp') || packet.arguments[2];

    stopRingtone();

    if (callState.isGroup) {
      const peer = callState.peers.get(fromUserId);
      if (peer && peer.pc && sdp) {
        await peer.pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp }));
        await flushQueuedCandidatesForPeer(peer);
      }
      return;
    }

    if (callState.pc && sdp) {
      await callState.pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp }));
      await flushQueuedCandidates();
    }
    return;
  }

  if (packet.target === 'IceCandidateReceived') {
    const fromUserId = unpackArg(packet.arguments, 1, 'senderId') || unpackArg(packet.arguments, 1, 'userId') || unpackArg(packet.arguments, 1) || callState.peerId;
    const candidateObj = unpackArg(packet.arguments, 2, 'candidate');
    const candidateStr = typeof candidateObj === 'string' ? candidateObj : candidateObj?.candidate || packet.arguments[2];
    const sdpMid = unpackArg(packet.arguments, 3, 'sdpMid') || packet.arguments[3] || '';
    const sdpMLineIndex = unpackArg(packet.arguments, 4, 'sdpMLineIndex') ?? packet.arguments[4] ?? 0;

    if (candidateStr) {
      const candData = {
        candidate: candidateStr,
        sdpMid: sdpMid,
        sdpMLineIndex: sdpMLineIndex
      };

      if (callState.isGroup) {
        const peer = callState.peers.get(fromUserId);
        if (peer && peer.pc && peer.pc.remoteDescription) {
          try {
            await peer.pc.addIceCandidate(new RTCIceCandidate(candData));
          } catch (e) {
            console.warn('Ошибка добавления ICE-кандидата для ' + fromUserId, e);
          }
        } else if (peer) {
          peer.queuedCandidates.push(candData);
        } else {
          callState.peers.set(fromUserId, {
            userId: fromUserId,
            fullName: 'Участник',
            pc: null,
            stream: null,
            queuedCandidates: [candData],
            audioDetector: null
          });
        }
        return;
      }

      if (callState.pc && callState.pc.remoteDescription) {
        try {
          await callState.pc.addIceCandidate(new RTCIceCandidate(candData));
        } catch (e) {
          console.warn('Ошибка добавления ICE-кандидата:', e);
        }
      } else {
        callState.queuedCandidates.push(candData);
      }
    }
    return;
  }

  if (packet.target === 'CallRejected') {
    clearTimeout(callState.callTimeout);
    const reason = unpackArg(packet.arguments, 3, 'reason') || packet.arguments[3];
    statusEl.textContent = reason === 'busy' ? 'Абонент занят' : 'Вызов отклонён';
    setTimeout(() => hangupCall(), reason === 'busy' ? 2200 : 800);
    return;
  }

  if (packet.target === 'CallEnded') {
    clearTimeout(callState.callTimeout);
    statusEl.textContent = 'Звонок завершён';
    hangupCall();
  }
}

// Запуск при открытии страницы
window.addEventListener('DOMContentLoaded', () => {
  startSignalR();
});

// Гарантированная остановка и сохранение записи при закрытии окна
window.addEventListener('beforeunload', () => {
  if (callState.isRecording) {
    stopRecording();
  }
});
