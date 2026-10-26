"use strict";
(function () {
  let counter = 0;
  let active = null;

  function escapeHelp(value) {
    return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function fieldHelp(label, text) {
    const id = 'field-help-' + (++counter);
    return `<span class="field-help"><button type="button" class="field-help-button" data-field-help aria-label="Пояснение: ${escapeHelp(label)}" aria-expanded="false" aria-controls="${id}">?</button><span id="${id}" class="field-help-popup" role="region" aria-label="Пояснение: ${escapeHelp(label)}" hidden>${escapeHelp(text)}</span></span>`;
  }

  function close(focus) {
    if (!active) return;
    const { button, popup, parent, next } = active;
    active = null;
    popup.hidden = true;
    popup.classList.remove('field-help-floating');
    popup.removeAttribute('style');
    button.setAttribute('aria-expanded', 'false');
    if (parent.isConnected) parent.insertBefore(popup, next?.parentNode === parent ? next : null);
    else popup.remove();
    if (focus && button.isConnected) button.focus();
  }

  function place(button, popup) {
    const gap = 8, margin = 12, rect = button.getBoundingClientRect();
    const width = Math.min(360, Math.max(220, window.innerWidth - margin * 2));
    popup.style.position = 'fixed';
    popup.style.width = width + 'px';
    popup.style.left = Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin)) + 'px';
    popup.style.top = (rect.bottom + gap) + 'px';
    popup.style.right = 'auto';
    popup.style.bottom = 'auto';
    popup.style.margin = '0';
    popup.style.maxHeight = Math.max(80, window.innerHeight - rect.bottom - gap - margin) + 'px';
    popup.style.overflowY = 'auto';
    const height = popup.getBoundingClientRect().height;
    if (rect.bottom + gap + height > window.innerHeight - margin) {
      popup.style.top = Math.max(margin, rect.top - gap - height) + 'px';
      popup.style.maxHeight = Math.max(80, rect.top - gap - margin) + 'px';
    }
  }

  function open(button) {
    if (active?.button === button) { close(true); return; }
    close(false);
    const popup = document.getElementById(button.getAttribute('aria-controls'));
    if (!popup) return;
    active = { button, popup, parent: popup.parentNode, next: popup.nextSibling };
    popup.hidden = false;
    popup.classList.add('field-help-floating');
    document.body.appendChild(popup);
    button.setAttribute('aria-expanded', 'true');
    place(button, popup);
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-field-help]');
    if (button) { event.preventDefault(); event.stopPropagation(); open(button); return; }
    if (event.target.closest('.field-help-popup')) return;
    close(false);
  });
  document.addEventListener('keydown', event => { if (event.key === 'Escape') close(true); });
  window.addEventListener('resize', () => { if (active) place(active.button, active.popup); });
  window.addEventListener('scroll', () => { if (active) place(active.button, active.popup); }, true);
  window.fieldHelp = fieldHelp;
})();
