/* Camera and microphone require a secure page, even when WebView permissions allow them. */
(() => {
  const target = new URL(location.href);
  const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(target.hostname);
  if (target.protocol === 'http:' && !loopback) {
    target.protocol = 'https:';
    location.replace(target.href);
  }
  function requireDevices() {
    if (!window.isSecureContext)
      throw new Error('Для камеры и микрофона откройте мессенджер по HTTPS с доверенным сертификатом.');
    if (!navigator.mediaDevices?.getUserMedia)
      throw new Error('Доступ к камере и микрофону недоступен в этом браузере.');
    return navigator.mediaDevices;
  }
  function explain(error, device) {
    switch (error?.name) {
      case 'NotAllowedError':
      case 'PermissionDeniedError':
      case 'SecurityError':
        return device + ': доступ запрещён. Разрешите его в браузере и в настройках конфиденциальности Windows.';
      case 'NotFoundError':
      case 'DevicesNotFoundError':
        return device + ': устройство не найдено. Проверьте подключение.';
      case 'NotReadableError':
      case 'TrackStartError':
        return device + ': устройство занято или недоступно Windows. Закройте другие приложения и повторите.';
      case 'OverconstrainedError':
        return device + ': выбранное устройство или параметры недоступны. Выберите системное устройство.';
      default:
        return device + ': ' + (error?.message || 'не удалось получить доступ к устройству.');
    }
  }
  async function inputs() {
    const media = requireDevices();
    let devices = await media.enumerateDevices();
    if (!devices.some(d => d.kind === 'audioinput' && d.label)) {
      const stream = await media.getUserMedia({audio: true});
      try { devices = await media.enumerateDevices(); }
      finally { stream.getTracks().forEach(track => track.stop()); }
    }
    return devices.filter(d => d.kind === 'audioinput');
  }
  window.HelperMedia = { requireDevices, explain, inputs };
})();
