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
  isRecording: false
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
const toastEl = $('toast');

const btnMic = $('btn-toggle-mic');
const btnCam = $('btn-toggle-cam');
const btnScreen = $('btn-toggle-screen');
const btnRecord = $('btn-record');
const btnHangup = $('btn-hangup');

function showToast(msg, duration = 4000) {
  toastEl.textContent = msg;
  toastEl.hidden = false;
  setTimeout(() => { toastEl.hidden = true; }, duration);
}

// 1. Инициализация интерфейса
targetNameEl.textContent = callState.targetName;
remoteFallbackName.textContent = callState.targetName;
remoteInitials.textContent = getInitials(callState.targetName);

// Кнопка записи доступна ТОЛЬКО инициатору звонка
if (callState.isCaller) {
  btnRecord.hidden = false;
} else {
  btnRecord.hidden = true;
}

if (!callState.isVideo) {
  btnCam.classList.add('muted');
  btnCam.querySelector('.icon').textContent = '🚫';
  $('local-pip-container').style.display = 'none';
}

function getInitials(name) {
  if (!name) return 'PH';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase();
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
  let audioTrack = null;
  let videoTrack = null;

  // 1. Попытка захватить микрофон
  try {
    if (navigator.mediaDevices?.getUserMedia) {
      const aStream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioTrack = aStream.getAudioTracks()[0] || null;
    }
  } catch (e) {
    console.warn('Микрофон недоступен:', e);
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
        console.warn('Камера не обнаружена:', err2);
      }
    }
  }

  // 3. Если микрофон отсутствует, создаём резервную тишину для работы WebRTC
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
    callState.isVideo = false;
    const pip = $('local-pip-container');
    if (pip) pip.style.display = 'none';
    btnCam.classList.add('muted');
    btnCam.querySelector('.icon').textContent = '🚫';
    if (callState.isVideo) showToast('Веб-камера не обнаружена. Звонок переключен в аудио-режим.');
  }

  if (!audioTrack) {
    showToast('Микрофон не обнаружен. Вы сможете только слушать собеседника.');
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

// 6. Подключение WebRTC PeerConnection
async function setupPeerConnection(callId, targetUserId) {
  const cfg = await getRtcConfig();
  const pc = new RTCPeerConnection(cfg);
  callState.pc = pc;
  callState.callId = callId;

  if (callState.localStream) {
    callState.localStream.getTracks().forEach(track => pc.addTrack(track, callState.localStream));
  }

  pc.ontrack = e => {
    if (e.streams && e.streams[0]) {
      callState.remoteStream = e.streams[0];
      remoteVideo.srcObject = e.streams[0];
      remoteAvatarFallback.hidden = true;
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
    } else if (pc.connectionState === 'disconnected' || pc.connectionState === 'failed') {
      statusEl.textContent = 'Связь прервана';
    }
  };

  return pc;
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
  clearInterval(callState.timerInterval);

  if (callState.callId && callState.ws?.readyState === WebSocket.OPEN) {
    callState.ws.send(JSON.stringify({ type: 1, target: 'EndCall', arguments: [callState.callId, callState.peerId || 0] }) + '\u001e');
  }

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

btnMic.onclick = () => {
  if (!callState.localStream) return;
  const audioTrack = callState.localStream.getAudioTracks()[0];
  if (audioTrack) {
    audioTrack.enabled = !audioTrack.enabled;
    btnMic.querySelector('.icon').textContent = audioTrack.enabled ? '🎤' : '🔇';
    btnMic.classList.toggle('muted', !audioTrack.enabled);
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
  }
};

btnScreen.onclick = async () => {
  if (!callState.pc) return;
  try {
    const screenStream = await navigator.mediaDevices.getDisplayMedia({ video: true });
    const screenTrack = screenStream.getVideoTracks()[0];
    const senders = callState.pc.getSenders();
    const vidSender = senders.find(s => s.track && s.track.kind === 'video');
    if (vidSender) {
      vidSender.replaceTrack(screenTrack);
      btnScreen.classList.add('muted');
      screenTrack.onended = () => {
        btnScreen.classList.remove('muted');
        const origTrack = callState.localStream?.getVideoTracks()[0];
        if (origTrack) vidSender.replaceTrack(origTrack);
      };
    }
  } catch (err) {
    showToast('Демонстрация экрана отменена');
  }
};

btnRecord.onclick = () => {
  if (callState.isRecording) {
    stopRecording();
  } else {
    startRecording();
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

    if (callState.isCaller) {
      statusEl.textContent = 'Вызов…';
      playRingtone();
      if (callState.peerId < 0) {
        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'StartGroupCall',
          arguments: [-callState.peerId, callState.isVideo, callState.callId]
        }) + '\u001e');
      } else {
        callState.ws.send(JSON.stringify({
          type: 1,
          target: 'StartCall',
          arguments: [callState.peerId, callState.isVideo, callState.callId]
        }) + '\u001e');
      }
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
  } catch (err) {
    statusEl.textContent = err.message;
  }
}

async function handleSignalRPacket(packet) {
  if (packet.target === 'CallAccepted') {
    const callId = unpackArg(packet.arguments, 0, 'callId') || callState.callId;
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
  }

  if (packet.target === 'SdpOfferReceived') {
    const callId = unpackArg(packet.arguments, 0, 'callId') || callState.callId;
    const fromUserId = unpackArg(packet.arguments, 1, 'senderId') || unpackArg(packet.arguments, 1, 'userId') || unpackArg(packet.arguments, 1) || callState.peerId;
    const sdp = unpackArg(packet.arguments, 2, 'sdp') || packet.arguments[2];

    let pc = callState.pc;
    if (!pc) pc = await setupPeerConnection(callId, fromUserId);
    await pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp }));
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    callState.ws.send(JSON.stringify({
      type: 1,
      target: 'SendSdpAnswer',
      arguments: [callId, fromUserId, answer.sdp]
    }) + '\u001e');
    stopRingtone();
  }

  if (packet.target === 'SdpAnswerReceived') {
    const sdp = unpackArg(packet.arguments, 2, 'sdp') || packet.arguments[2];
    if (callState.pc && sdp) {
      await callState.pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp }));
    }
  }

  if (packet.target === 'IceCandidateReceived') {
    const candidateObj = unpackArg(packet.arguments, 2, 'candidate');
    const candidateStr = typeof candidateObj === 'string' ? candidateObj : candidateObj?.candidate || packet.arguments[2];
    const sdpMid = unpackArg(packet.arguments, 3, 'sdpMid') || packet.arguments[3] || '';
    const sdpMLineIndex = unpackArg(packet.arguments, 4, 'sdpMLineIndex') ?? packet.arguments[4] ?? 0;

    if (callState.pc && candidateStr) {
      try {
        await callState.pc.addIceCandidate(new RTCIceCandidate({
          candidate: candidateStr,
          sdpMid: sdpMid,
          sdpMLineIndex: sdpMLineIndex
        }));
      } catch {}
    }
  }

  if (packet.target === 'CallRejected' || packet.target === 'CallEnded') {
    statusEl.textContent = packet.target === 'CallRejected' ? 'Вызов отклонён' : 'Звонок завершён';
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
